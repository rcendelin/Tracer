using Tracer.Domain.Entities;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="ChangeEvent"/> entities (change history).
/// </summary>
public interface IChangeEventRepository
{
    /// <summary>
    /// Gets a change event by its ID. Returns <see langword="null"/> if not found.
    /// </summary>
    Task<ChangeEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Lists change events with optional exact severity and profile filters, ordered by detection date descending.
    /// </summary>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="severity">Optional exact severity filter. <see langword="null"/> returns all severities.</param>
    /// <param name="profileId">Optional profile ID filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyCollection<ChangeEvent>> ListAsync(
        int page, int pageSize,
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts change events with optional exact severity and profile filters.
    /// </summary>
    Task<int> CountAsync(
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts change events whose <see cref="ChangeEvent.DetectedAt"/> is greater
    /// than or equal to <paramref name="detectedAfter"/>. Typically used to
    /// compute "changes detected today" on the validation dashboard.
    /// </summary>
    /// <param name="detectedAfter">Inclusive lower bound. Typically start-of-day UTC.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountSinceAsync(DateTimeOffset detectedAfter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams up to <paramref name="maxRows"/> change events matching the specified
    /// filters, ordered by <c>DetectedAt DESC</c>. Intended for batch export (B-81) —
    /// caller is responsible for enforcing the absolute row cap.
    /// </summary>
    /// <remarks>
    /// Implementations must use <c>AsNoTracking()</c> and SQL-side <c>Take(maxRows)</c>
    /// so rows are streamed from the reader rather than materialised to a list.
    /// The returned enumerable MUST be consumed within the same DbContext scope.
    /// </remarks>
    /// <param name="maxRows">Absolute row cap (1 ≤ maxRows ≤ 10_000).</param>
    /// <param name="severity">Optional exact severity filter.</param>
    /// <param name="profileId">Optional profile ID filter.</param>
    /// <param name="from">Inclusive lower bound on <c>DetectedAt</c>.</param>
    /// <param name="to">Exclusive upper bound on <c>DetectedAt</c>.</param>
    IAsyncEnumerable<ChangeEvent> StreamAsync(
        int maxRows,
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);
}
