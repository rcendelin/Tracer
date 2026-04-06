namespace Tracer.Infrastructure.Providers.AbnLookup;

/// <summary>
/// HTTP client for Australian Business Register (ABN Lookup) API.
/// Base URL: <c>https://abr.business.gov.au/json</c>
/// Auth: GUID parameter.
/// </summary>
internal interface IAbnLookupClient
{
    Task<AbnDetailsResponse?> GetByAbnAsync(string abn, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AbnSearchResult>> SearchByNameAsync(string name, CancellationToken cancellationToken);
}
