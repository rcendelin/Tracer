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
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ChangeEvent> result = _byId.Values
            .Where(e => severity is null || e.Severity == severity)
            .Where(e => profileId is null || e.CompanyProfileId == profileId)
            .Where(e => since is null || e.DetectedAt >= since)
            .OrderByDescending(e => e.DetectedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var count = _byId.Values
            .Where(e => severity is null || e.Severity == severity)
            .Where(e => profileId is null || e.CompanyProfileId == profileId)
            .Count(e => since is null || e.DetectedAt >= since);
        return Task.FromResult(count);
    }

    public Task<int> CountSinceAsync(DateTimeOffset detectedAfter, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Count(e => e.DetectedAt >= detectedAfter));

    public IAsyncEnumerable<ChangeEvent> StreamAsync(
        int maxRows,
        ChangeSeverity? severity = null,
        Guid? profileId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (maxRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        var query = _byId.Values
            .Where(e => severity is null || e.Severity == severity)
            .Where(e => profileId is null || e.CompanyProfileId == profileId)
            .Where(e => from is null || e.DetectedAt >= from)
            .Where(e => to is null || e.DetectedAt < to)
            .OrderByDescending(e => e.DetectedAt)
            .Take(maxRows);
        return ToAsync(query, cancellationToken);

        static async IAsyncEnumerable<ChangeEvent> ToAsync(
            IEnumerable<ChangeEvent> source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in source)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }

    public Task<IReadOnlyList<ChangeTrendBucketRow>> GetMonthlyTrendAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toExclusive <= fromInclusive)
            return Task.FromResult<IReadOnlyList<ChangeTrendBucketRow>>(Array.Empty<ChangeTrendBucketRow>());

        IReadOnlyList<ChangeTrendBucketRow> result = _byId.Values
            .Where(e => e.DetectedAt >= fromInclusive && e.DetectedAt < toExclusive)
            .GroupBy(e => new { e.DetectedAt.Year, e.DetectedAt.Month, e.Severity })
            .Select(g => new ChangeTrendBucketRow
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Severity = g.Key.Severity,
                Count = g.Count(),
            })
            .ToList();
        return Task.FromResult(result);
    }
}
