using Tracer.Domain.Entities;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Repository for the <see cref="TraceRequest"/> aggregate root.
/// </summary>
public interface ITraceRequestRepository
{
    /// <summary>
    /// Persists a new trace request.
    /// </summary>
    /// <param name="request">The trace request to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(TraceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a trace request by its ID.
    /// </summary>
    /// <param name="id">The trace request ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trace request, or <see langword="null"/> if not found.</returns>
    Task<TraceRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists trace requests with pagination and optional filters.
    /// </summary>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="from">Optional start date filter (inclusive).</param>
    /// <param name="to">Optional end date filter (inclusive).</param>
    /// <param name="search">Optional search term (matches CompanyName, RegistrationId).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paged collection of trace requests, ordered by creation date descending.</returns>
    Task<IReadOnlyCollection<TraceRequest>> ListAsync(
        int page, int pageSize,
        TraceStatus? status = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts trace requests matching the specified filters.
    /// </summary>
    Task<int> CountAsync(
        TraceStatus? status = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? search = null,
        CancellationToken cancellationToken = default);
}
