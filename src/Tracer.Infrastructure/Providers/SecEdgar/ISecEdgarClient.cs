namespace Tracer.Infrastructure.Providers.SecEdgar;

/// <summary>
/// HTTP client for SEC EDGAR API.
/// Base URL: <c>https://efts.sec.gov</c> (search) and <c>https://data.sec.gov</c> (submissions).
/// Requires User-Agent header. Rate limit: 10 requests/second.
/// </summary>
internal interface ISecEdgarClient
{
    Task<IReadOnlyCollection<EdgarSearchSource>> SearchByNameAsync(string name, CancellationToken cancellationToken);
    Task<EdgarSubmissions?> GetSubmissionsAsync(string cik, CancellationToken cancellationToken);
}
