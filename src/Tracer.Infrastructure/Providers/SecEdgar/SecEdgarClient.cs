using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.SecEdgar;

/// <summary>
/// HTTP client for SEC EDGAR.
/// Uses two endpoints: efts.sec.gov (search) and data.sec.gov (submissions).
/// </summary>
internal sealed partial class SecEdgarClient : ISecEdgarClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SecEdgarClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SecEdgarClient(HttpClient httpClient, ILogger<SecEdgarClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<EdgarSearchSource>> SearchByNameAsync(
        string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        var encodedName = Uri.EscapeDataString(name.Trim());
        var url = new Uri(
            $"https://efts.sec.gov/LATEST/search-index?q=%22{encodedName}%22&forms=10-K&dateRange=custom&startdt=2020-01-01&enddt=2030-01-01",
            UriKind.Absolute);

        LogSearching(name);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<EdgarSearchResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Hits?.Items?
            .Select(h => h.Source)
            .Where(s => s is not null)
            .Cast<EdgarSearchSource>()
            .DistinctBy(s => s.EntityId)
            .Take(5)
            .ToList() ?? [];
    }

    public async Task<EdgarSubmissions?> GetSubmissionsAsync(
        string cik, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cik, nameof(cik));

        // CIK must be zero-padded to 10 digits
        var paddedCik = cik.PadLeft(10, '0');
        var url = new Uri($"https://data.sec.gov/submissions/CIK{paddedCik}.json", UriKind.Absolute);

        LogFetching(cik);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<EdgarSubmissions>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "SEC EDGAR: Searching '{Name}'")]
    private partial void LogSearching(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SEC EDGAR: Fetching CIK {Cik}")]
    private partial void LogFetching(string cik);
}
