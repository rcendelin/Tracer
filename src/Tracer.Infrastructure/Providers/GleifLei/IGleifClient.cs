namespace Tracer.Infrastructure.Providers.GleifLei;

/// <summary>
/// HTTP client abstraction for the GLEIF LEI API.
/// Base URL: <c>https://api.gleif.org/api/v1</c>
/// </summary>
internal interface IGleifClient
{
    /// <summary>
    /// Searches for LEI records by company name and optional country filter.
    /// </summary>
    Task<IReadOnlyCollection<GleifLeiRecord>> SearchByNameAsync(string name, string? country, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a LEI record by its LEI code.
    /// </summary>
    Task<GleifLeiRecord?> GetByLeiAsync(string lei, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the direct parent relationship for a LEI.
    /// </summary>
    Task<GleifRelationshipNode?> GetDirectParentAsync(string lei, CancellationToken cancellationToken);
}
