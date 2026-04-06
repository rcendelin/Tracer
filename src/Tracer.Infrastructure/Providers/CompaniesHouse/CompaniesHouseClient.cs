using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Infrastructure.Providers.CompaniesHouse;

/// <summary>
/// HTTP client for the UK Companies House API.
/// Auth via Basic authentication (API key as username).
/// </summary>
internal sealed partial class CompaniesHouseClient : ICompaniesHouseClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CompaniesHouseClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CompaniesHouseClient(HttpClient httpClient, ILogger<CompaniesHouseClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<CompanySearchItem>> SearchByNameAsync(
        string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        var encodedName = Uri.EscapeDataString(name.Trim());
        var url = new Uri($"search/companies?q={encodedName}&items_per_page=5", UriKind.Relative);

        LogSearching(name);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<CompanySearchResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Items ?? [];
    }

    public async Task<CompaniesHouseCompanyProfile?> GetCompanyAsync(
        string companyNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNumber, nameof(companyNumber));

        var url = new Uri($"company/{Uri.EscapeDataString(companyNumber)}", UriKind.Relative);

        LogFetching(companyNumber);

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<CompaniesHouseCompanyProfile>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Companies House: Searching '{Name}'")]
    private partial void LogSearching(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Companies House: Fetching company {CompanyNumber}")]
    private partial void LogFetching(string companyNumber);
}
