using System.Collections.Concurrent;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="IChangeEventRepository"/> used by E2E tests.
/// </summary>
internal sealed class InMemoryChangeEventRepository : IChangeEventRepository
{
    private readonly ConcurrentDictionary<Guid, ChangeEvent> _byId = new();

    public IReadOnlyCollection<ChangeEvent> All => _byId.Values.ToList();

    public Task<ChangeEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var e) ? e : null);

    public Task AddAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeEvent);
        _byId[changeEvent.Id] = changeEvent;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ChangeEvent>> ListByProfileAsync(
        Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ChangeEvent> result = _byId.Values
            .Where(e => e.CompanyProfileId == companyProfileId)
            .OrderByDescending(e => e.DetectedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyCollection<ChangeEvent>> ListBySeverityAsync(
        ChangeSeverity minSeverity, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ChangeEvent> result = _byId.Values
            .Where(e => (int)e.Severity >= (int)minSeverity)
            .OrderByDescending(e => e.DetectedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> CountByProfileAsync(Guid companyProfileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Count(e => e.CompanyProfileId == companyProfileId));

    public Task<int> CountBySeverityAsync(ChangeSeverity minSeverity, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Count(e => (int)e.Severity >= (int)minSeverity));

    public Task<IReadOnlyCollection<ChangeEvent>> ListAsync(
        int page, int pageSize,
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ChangeEvent> result = _byId.Values
            .Where(e => severity is null || e.Severity == severity)
            .Where(e => profileId is null || e.CompanyProfileId == profileId)
            .OrderByDescending(e => e.DetectedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        CancellationToken cancellationToken = default)
    {
        var count = _byId.Values
            .Where(e => severity is null || e.Severity == severity)
            .Count(e => profileId is null || e.CompanyProfileId == profileId);
        return Task.FromResult(count);
    }
}
