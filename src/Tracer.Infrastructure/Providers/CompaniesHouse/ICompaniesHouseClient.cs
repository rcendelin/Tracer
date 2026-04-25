namespace Tracer.Infrastructure.Providers.CompaniesHouse;

/// <summary>
/// HTTP client for UK Companies House API.
/// Base URL: <c>https://api.company-information.service.gov.uk</c>
/// Auth: Basic (API key as username, no password).
/// Rate limit: 600 requests per 5 minutes.
/// </summary>
internal interface ICompaniesHouseClient
{
    Task<IReadOnlyCollection<CompanySearchItem>> SearchByNameAsync(string name, CancellationToken cancellationToken);
    Task<CompaniesHouseCompanyProfile?> GetCompanyAsync(string companyNumber, CancellationToken cancellationToken);
}
