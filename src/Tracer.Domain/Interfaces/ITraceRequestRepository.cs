using Tracer.Domain.Entities;

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
    /// Lists trace requests with pagination.
    /// </summary>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paged collection of trace requests, ordered by creation date descending.</returns>
    Task<IReadOnlyCollection<TraceRequest>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
