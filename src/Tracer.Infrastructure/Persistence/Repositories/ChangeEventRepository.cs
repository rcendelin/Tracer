using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IChangeEventRepository"/>.
/// </summary>
internal sealed class ChangeEventRepository : IChangeEventRepository
{
    private readonly TracerDbContext _db;

    public ChangeEventRepository(TracerDbContext db)
    {
        _db = db;
    }

    public async Task<ChangeEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.ChangeEvents
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
    {
        await _db.ChangeEvents.AddAsync(changeEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<ChangeEvent>> ListByProfileAsync(
        Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken)
    {
        return await _db.ChangeEvents
            .AsNoTracking()
            .Where(e => e.CompanyProfileId == companyProfileId)
            .OrderByDescending(e => e.DetectedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<ChangeEvent>> ListBySeverityAsync(
        ChangeSeverity minSeverity, int page, int pageSize, CancellationToken cancellationToken)
    {
        return await _db.ChangeEvents
            .AsNoTracking()
            .Where(e => e.Severity >= minSeverity)
            .OrderByDescending(e => e.DetectedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountByProfileAsync(Guid companyProfileId, CancellationToken cancellationToken)
    {
        return await _db.ChangeEvents
            .AsNoTracking()
            .Where(e => e.CompanyProfileId == companyProfileId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountBySeverityAsync(ChangeSeverity minSeverity, CancellationToken cancellationToken)
    {
        return await _db.ChangeEvents
            .AsNoTracking()
            .Where(e => e.Severity >= minSeverity)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
