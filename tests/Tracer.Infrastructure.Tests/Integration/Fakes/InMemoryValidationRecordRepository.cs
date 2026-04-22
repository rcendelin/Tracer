using System.Collections.Concurrent;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="IValidationRecordRepository"/> used by E2E tests.
/// </summary>
internal sealed class InMemoryValidationRecordRepository : IValidationRecordRepository
{
    private readonly ConcurrentBag<ValidationRecord> _all = [];

    public IReadOnlyCollection<ValidationRecord> All => _all.ToArray();

    public Task AddAsync(ValidationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _all.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ValidationRecord>> ListByProfileAsync(
        Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ValidationRecord> result = _all
            .Where(r => r.CompanyProfileId == companyProfileId)
            .OrderByDescending(r => r.ValidatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }
}
