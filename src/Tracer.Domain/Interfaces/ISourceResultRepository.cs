using Tracer.Domain.Entities;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="SourceResult"/> entities (provider execution audit trail).
/// </summary>
public interface ISourceResultRepository
{
    /// <summary>
    /// Persists a new source result.
    /// </summary>
    Task AddAsync(SourceResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists source results for a specific trace request.
    /// </summary>
    Task<IReadOnlyCollection<SourceResult>> ListByTraceRequestAsync(Guid traceRequestId, CancellationToken cancellationToken = default);
}
