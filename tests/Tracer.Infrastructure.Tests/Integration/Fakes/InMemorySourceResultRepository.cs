using System.Collections.Concurrent;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="ISourceResultRepository"/> used by E2E tests.
/// </summary>
internal sealed class InMemorySourceResultRepository : ISourceResultRepository
{
    private readonly ConcurrentBag<SourceResult> _all = [];

    public IReadOnlyCollection<SourceResult> All => _all.ToArray();

    public Task AddAsync(SourceResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        _all.Add(result);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<SourceResult>> ListByTraceRequestAsync(
        Guid traceRequestId, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<SourceResult> result = _all
            .Where(r => r.TraceRequestId == traceRequestId)
            .OrderBy(r => r.CompletedAt)
            .ToList();
        return Task.FromResult(result);
    }
}
