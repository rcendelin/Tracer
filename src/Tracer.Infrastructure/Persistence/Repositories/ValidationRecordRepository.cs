using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IValidationRecordRepository"/>.
/// </summary>
internal sealed class ValidationRecordRepository : IValidationRecordRepository
{
    private readonly TracerDbContext _db;

    public ValidationRecordRepository(TracerDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ValidationRecord record, CancellationToken cancellationToken)
    {
        await _db.ValidationRecords.AddAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<ValidationRecord>> ListByProfileAsync(
        Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken)
    {
        return await _db.ValidationRecords
            .AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId)
            .OrderByDescending(v => v.ValidatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
