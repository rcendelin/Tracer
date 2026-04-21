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

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.CompanyProfiles
            .AnyAsync(p => p.Id == id, cancellationToken)
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
        string? search, string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(
            _db.CompanyProfiles.AsNoTracking(),
            search, country, minConfidence, maxConfidence, validatedBefore, includeArchived);

        return await query
            .OrderByDescending(p => p.TraceCount)
            .ThenByDescending(p => p.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(
        string country, int maxCount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country, nameof(country));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount, nameof(maxCount));

        return await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived && p.Country == country)
            .OrderByDescending(p => p.TraceCount)
            .Take(maxCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(
        string? search, string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(
            _db.CompanyProfiles.AsNoTracking(),
            search, country, minConfidence, maxConfidence, validatedBefore, includeArchived);

        return await query.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountRevalidationCandidatesAsync(CancellationToken cancellationToken)
    {
        return await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<double> AverageDaysSinceLastValidationAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // EF Core cannot translate DateTimeOffset subtraction to SQL for all providers,
        // so we pull the two timestamps and compute the age client-side. Because this is
        // bounded by the number of non-archived profiles and is a dashboard-only query,
        // the round-trip cost is acceptable; a production-scale replacement would move
        // this to a pre-aggregated column or a materialised view.
        var rows = await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .Select(p => new { p.LastValidatedAt, p.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return 0;

        double totalDays = 0;
        foreach (var row in rows)
        {
            var reference = row.LastValidatedAt ?? row.CreatedAt;
            var age = (now - reference).TotalDays;
            if (age < 0) age = 0;
            totalDays += age;
        }

        return totalDays / rows.Count;
    }

    private static IQueryable<CompanyProfile> ApplyFilters(
        IQueryable<CompanyProfile> query,
        string? search, string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived)
    {
        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Search by registration ID and normalized key (both are regular columns — safe for SQL translation).
            // LegalName is stored in a JSON column; querying it would require a computed column index
            // which is outside the scope of this block. Name search can be added in a future block.
            query = query.Where(p =>
                (p.RegistrationId != null && p.RegistrationId.Contains(search)) ||
                p.NormalizedKey.Contains(search));
        }

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
