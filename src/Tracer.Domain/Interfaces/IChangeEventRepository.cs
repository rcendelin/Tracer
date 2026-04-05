using Tracer.Domain.Entities;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ChangeEvent"/> entities (change history).
/// </summary>
public interface IChangeEventRepository
{
    /// <summary>
    /// Persists a new change event.
    /// </summary>
    /// <param name="changeEvent">The change event to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists change events for a specific company profile, ordered by detection date descending.
    /// </summary>
    /// <param name="companyProfileId">The company profile ID.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<ChangeEvent>> ListByProfileAsync(Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists change events filtered by minimum severity, ordered by detection date descending.
    /// </summary>
    /// <remarks>
    /// Relies on <see cref="ChangeSeverity"/> being ordered by ascending numeric value
    /// (Cosmetic=0 &lt; Minor=1 &lt; Major=2 &lt; Critical=3).
    /// Implementations must use <c>&gt;= (int)minSeverity</c> in their query predicate.
    /// </remarks>
    /// <param name="minSeverity">The minimum severity to include.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<ChangeEvent>> ListBySeverityAsync(ChangeSeverity minSeverity, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Counts change events for a specific company profile.</summary>
    Task<int> CountByProfileAsync(Guid companyProfileId, CancellationToken cancellationToken = default);

    /// <summary>Counts change events matching the minimum severity filter.</summary>
    Task<int> CountBySeverityAsync(ChangeSeverity minSeverity, CancellationToken cancellationToken = default);
}
