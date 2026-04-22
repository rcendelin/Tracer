using System.Collections.Concurrent;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="ITraceRequestRepository"/> used by E2E tests.
/// </summary>
internal sealed class InMemoryTraceRequestRepository : ITraceRequestRepository
{
    private readonly ConcurrentDictionary<Guid, TraceRequest> _byId = new();

    public IReadOnlyCollection<TraceRequest> All => _byId.Values.ToList();

    public Task AddAsync(TraceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _byId[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task<TraceRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var r) ? r : null);

    public Task<IReadOnlyCollection<TraceRequest>> ListAsync(
        int page, int pageSize,
        TraceStatus? status = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<TraceRequest> result = _byId.Values
            .Where(r => status is null || r.Status == status)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        TraceStatus? status = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var count = _byId.Values.Count(r => status is null || r.Status == status);
        return Task.FromResult(count);
    }
}
