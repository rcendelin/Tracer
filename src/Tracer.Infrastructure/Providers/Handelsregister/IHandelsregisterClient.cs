namespace Tracer.Infrastructure.Providers.Handelsregister;

/// <summary>
/// Abstraction for querying the German commercial register (Handelsregister.de).
/// Supports search by company name and by register number.
/// </summary>
internal interface IHandelsregisterClient
{
    /// <summary>
    /// Searches Handelsregister by company name (keyword search).
    /// </summary>
    /// <param name="companyName">The company name or keywords to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An array of matching search results, or <see langword="null"/> if no results were found
    /// or the search page could not be retrieved.
    /// </returns>
    Task<IReadOnlyList<HandelsregisterSearchResult>?> SearchByNameAsync(string companyName, CancellationToken ct);

    /// <summary>
    /// Retrieves detailed company information by register number.
    /// </summary>
    /// <param name="registerType">The register type (e.g. "HRB", "HRA").</param>
    /// <param name="registerNumber">The register number.</param>
    /// <param name="court">
    /// The registration court identifier, or <see langword="null"/> to search all courts.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Detailed company information, or <see langword="null"/> if not found.
    /// </returns>
    Task<HandelsregisterCompanyDetail?> GetByRegisterNumberAsync(
        string registerType,
        string registerNumber,
        string? court,
        CancellationToken ct);
}
