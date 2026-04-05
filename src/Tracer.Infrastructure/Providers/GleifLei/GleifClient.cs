using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.GleifLei;

/// <summary>
/// HTTP client for the GLEIF LEI API (<c>https://api.gleif.org/api/v1</c>).
/// Free, no API key required. CC0 licensed data.
/// </summary>
internal sealed partial class GleifClient : IGleifClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GleifClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public GleifClient(HttpClient httpClient, ILogger<GleifClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<GleifLeiRecord>> SearchByNameAsync(
        string name, string? country, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        var encodedName = Uri.EscapeDataString(name.Trim());
        var url = $"lei-records?filter[entity.legalName]={encodedName}&page[size]=5";

        if (!string.IsNullOrWhiteSpace(country))
            url += $"&filter[entity.legalAddress.country]={Uri.EscapeDataString(country)}";

        LogSearching(name, country);

        var response = await _httpClient.GetAsync(new Uri(url, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<GleifSearchResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Data ?? [];
    }

    public async Task<GleifLeiRecord?> GetByLeiAsync(string lei, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lei, nameof(lei));

        LogFetchingLei(lei);

        var response = await _httpClient.GetAsync(
            new Uri($"lei-records/{Uri.EscapeDataString(lei)}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content
            .ReadFromJsonAsync<GleifSingleResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return wrapper?.Data;
    }

    public async Task<GleifRelationshipNode?> GetDirectParentAsync(string lei, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lei, nameof(lei));

        try
        {
            var response = await _httpClient.GetAsync(
                new Uri($"lei-records/{Uri.EscapeDataString(lei)}/direct-parent", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<GleifRelationshipResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return result?.Data?.FirstOrDefault()?.Attributes?.Relationship?.EndNode;
        }
        catch (HttpRequestException ex)
        {
            LogParentError(ex, lei);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "GLEIF: Searching '{Name}' country={Country}")]
    private partial void LogSearching(string name, string? country);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GLEIF: Fetching LEI {Lei}")]
    private partial void LogFetchingLei(string lei);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GLEIF: Parent lookup failed for LEI {Lei}")]
    private partial void LogParentError(Exception ex, string lei);
}

