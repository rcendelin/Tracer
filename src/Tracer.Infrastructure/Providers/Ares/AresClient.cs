using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.Ares;

/// <summary>
/// HTTP client for the Czech ARES API.
/// Configured with resilience policies (retry + timeout) via <c>Microsoft.Extensions.Http.Resilience</c>.
/// </summary>
internal sealed partial class AresClient : IAresClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AresClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AresClient(HttpClient httpClient, ILogger<AresClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AresEkonomickySubjekt?> GetByIcoAsync(string ico, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ico, nameof(ico));

        if (!IcoRegex().IsMatch(ico))
            throw new ArgumentException("IČO must be 1–8 digits.", nameof(ico));

        var url = new Uri($"ekonomicke-subjekty/{ico}", UriKind.Relative);

        LogFetchingByIco(ico);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            LogIcoNotFound(ico);
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<AresEkonomickySubjekt>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> SearchByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        var url = new Uri("ekonomicke-subjekty/vyhledat", UriKind.Relative);
        var requestBody = new { obchodniJmeno = name.Trim(), start = 0, pocet = 1 };

        LogSearchingByName(name);

        try
        {
            var response = await _httpClient
                .PostAsJsonAsync(url, requestBody, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<AresSearchResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            var firstMatch = result?.EkonomickeSubjekty?.FirstOrDefault();

            if (firstMatch?.Ico is null)
            {
                LogNameNotFound(name);
                return null;
            }

            LogNameFound(name, firstMatch.Ico);
            return firstMatch.Ico;
        }
        catch (HttpRequestException ex)
        {
            LogSearchError(ex, name);
            return null;
        }
    }

    [GeneratedRegex(@"^\d{1,8}$")]
    private static partial Regex IcoRegex();

    [LoggerMessage(Level = LogLevel.Debug, Message = "ARES: Fetching by IČO {Ico}")]
    private partial void LogFetchingByIco(string ico);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ARES: IČO {Ico} not found")]
    private partial void LogIcoNotFound(string ico);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ARES: Searching by name '{Name}'")]
    private partial void LogSearchingByName(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ARES: Name '{Name}' not found")]
    private partial void LogNameNotFound(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ARES: Name '{Name}' matched IČO {Ico}")]
    private partial void LogNameFound(string name, string ico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ARES: Search error for name '{Name}'")]
    private partial void LogSearchError(Exception ex, string name);
}
