namespace Tracer.Infrastructure.Providers.Ares;

/// <summary>
/// HTTP client abstraction for the Czech ARES (Administrativní registr ekonomických subjektů) API.
/// Base URL: <c>https://ares.gov.cz/ekonomicke-subjekty-v-be/rest</c>
/// </summary>
internal interface IAresClient
{
    /// <summary>
    /// Gets a company by its IČO (registration number).
    /// </summary>
    /// <param name="ico">The IČO, e.g. <c>"00027006"</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The company data, or <see langword="null"/> if not found.</returns>
    Task<AresEkonomickySubjekt?> GetByIcoAsync(string ico, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for companies by name. Returns the first match's IČO.
    /// </summary>
    /// <param name="name">The company name to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IČO of the best match, or <see langword="null"/> if not found.</returns>
    Task<string?> SearchByNameAsync(string name, CancellationToken cancellationToken);
}
