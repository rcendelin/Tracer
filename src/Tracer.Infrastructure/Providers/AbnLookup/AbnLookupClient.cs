using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.AbnLookup;

/// <summary>
/// HTTP client for ABN Lookup API.
/// Uses JSON endpoints with GUID authentication passed via query parameter.
/// </summary>
internal sealed partial class AbnLookupClient : IAbnLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AbnLookupClient> _logger;
    private readonly string _guid;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AbnLookupClient(HttpClient httpClient, ILogger<AbnLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Read GUID from custom header (set in DI registration)
        _guid = httpClient.DefaultRequestHeaders.TryGetValues("X-Abn-Guid", out var values)
            ? values.First()
            : string.Empty;
    }

    public async Task<AbnDetailsResponse?> GetByAbnAsync(string abn, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abn, nameof(abn));

        var url = new Uri($"AbnDetails.aspx?abn={Uri.EscapeDataString(abn)}&callback=&guid={_guid}", UriKind.Relative);

        LogFetching(abn);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<AbnDetailsResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        // ABN Lookup returns a response with Message for not-found instead of 404
        if (!string.IsNullOrWhiteSpace(result?.Message))
            return null;

        return result;
    }

    public async Task<IReadOnlyCollection<AbnSearchResult>> SearchByNameAsync(
        string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        var encodedName = Uri.EscapeDataString(name.Trim());
        var url = new Uri($"MatchingNames.aspx?name={encodedName}&callback=&guid={_guid}&maxResults=5", UriKind.Relative);

        LogSearching(name);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<AbnSearchResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Names ?? [];
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "ABN Lookup: Fetching ABN {Abn}")]
    private partial void LogFetching(string abn);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ABN Lookup: Searching '{Name}'")]
    private partial void LogSearching(string name);
}
