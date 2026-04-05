using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICompanyProfileRepository"/>.
/// </summary>
internal sealed class CompanyProfileRepository : ICompanyProfileRepository
{
    private readonly TracerDbContext _db;

    public CompanyProfileRepository(TracerDbContext db)
    {
        _db = db;
    }

    public async Task<CompanyProfile?> FindByKeyAsync(string normalizedKey, CancellationToken cancellationToken)
    {
        return await _db.CompanyProfiles
            .FirstOrDefaultAsync(p => p.NormalizedKey == normalizedKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CompanyProfile?> FindByRegistrationIdAsync(string registrationId, string country, CancellationToken cancellationToken)
    {
        return await _db.CompanyProfiles
            .FirstOrDefaultAsync(p => p.RegistrationId == registrationId && p.Country == country, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
        var exists = await _db.CompanyProfiles
            .AnyAsync(p => p.Id == profile.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
            await _db.CompanyProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        else
            _db.CompanyProfiles.Update(profile);
    }

    public async Task<IReadOnlyCollection<CompanyProfile>> GetRevalidationQueueAsync(int maxCount, CancellationToken cancellationToken)
    {
        // Priority: highest TraceCount first, then oldest LastValidatedAt
        return await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .OrderByDescending(p => p.TraceCount)
            .ThenBy(p => p.LastValidatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CompanyProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.CompanyProfiles
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<CompanyProfile>> ListAsync(
        int page, int pageSize,
        string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(
            _db.CompanyProfiles.AsNoTracking(),
            country, minConfidence, maxConfidence, validatedBefore, includeArchived);

        return await query
            .OrderByDescending(p => p.TraceCount)
            .ThenByDescending(p => p.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(
        string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(
            _db.CompanyProfiles.AsNoTracking(),
            country, minConfidence, maxConfidence, validatedBefore, includeArchived);

        return await query.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IQueryable<CompanyProfile> ApplyFilters(
        IQueryable<CompanyProfile> query,
        string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived)
    {
        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(p => p.Country == country);

        if (minConfidence.HasValue)
        {
            var min = minConfidence.Value;
            query = query.Where(p => EF.Property<double?>(p, "OverallConfidence") >= min);
        }

        if (maxConfidence.HasValue)
        {
            var max = maxConfidence.Value;
            query = query.Where(p => EF.Property<double?>(p, "OverallConfidence") <= max);
        }

        if (validatedBefore.HasValue)
            query = query.Where(p => p.LastValidatedAt == null || p.LastValidatedAt < validatedBefore.Value);

        return query;
    }
}
