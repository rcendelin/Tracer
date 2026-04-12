namespace Tracer.Infrastructure.Providers.StateSos;

/// <summary>
/// HTTP client abstraction for US Secretary of State business registries.
/// Dispatches to per-state adapters based on <paramref name="stateCode"/>.
/// </summary>
internal interface IStateSosClient
{
    /// <summary>
    /// Searches for a company across one or more US state registries.
    /// </summary>
    /// <param name="companyName">The company name to search for.</param>
    /// <param name="stateCode">
    /// The two-letter US state code to search (e.g. "CA", "DE", "NY"),
    /// or <see langword="null"/> to search all supported states.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Search results from matching state adapters, or <see langword="null"/> if no results were found.
    /// </returns>
    Task<IReadOnlyList<StateSosSearchResult>?> SearchAsync(
        string companyName,
        string? stateCode,
        CancellationToken ct);
}
