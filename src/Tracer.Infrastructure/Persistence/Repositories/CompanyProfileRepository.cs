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

    public async Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(
        string country, int maxCount, int minTraceCount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country, nameof(country));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount, nameof(maxCount));
        ArgumentOutOfRangeException.ThrowIfNegative(minTraceCount, nameof(minTraceCount));

        return await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived && p.Country == country && p.TraceCount >= minTraceCount)
            .OrderByDescending(p => p.TraceCount)
            .Take(maxCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<CompanyProfile>> ListTopByTraceCountAsync(
        int maxCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount, nameof(maxCount));

        return await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived)
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

    public async Task<double> GetAverageConfidenceAsync(
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        // Shadow property "OverallConfidence" maps the Confidence value object to a nullable double column;
        // SQL Server AVG ignores NULLs, so EF Core translates this to `AVG([OverallConfidence])` with
        // NULL handling built in. The outer null-coalesce catches the empty-set case where AVG returns NULL.
        var query = _db.CompanyProfiles.AsNoTracking();
        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);

        var average = await query
            .Select(p => EF.Property<double?>(p, "OverallConfidence"))
            .AverageAsync(cancellationToken)
            .ConfigureAwait(false);

        return average ?? 0.0;
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

    public IAsyncEnumerable<CompanyProfile> StreamAsync(
        int maxRows,
        string? search, string? country, double? minConfidence, double? maxConfidence,
        DateTimeOffset? validatedBefore, bool includeArchived,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        var query = ApplyFilters(
            _db.CompanyProfiles.AsNoTracking(),
            search, country, minConfidence, maxConfidence, validatedBefore, includeArchived);

        return query
            .OrderByDescending(p => p.TraceCount)
            .ThenByDescending(p => p.CreatedAt)
            .Take(maxRows)
            .AsAsyncEnumerable();
    }

    public async Task<int> ArchiveStaleAsync(
        DateTimeOffset enrichedBefore,
        int maxTraceCount,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxTraceCount, nameof(maxTraceCount));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize, nameof(batchSize));

        // ExecuteUpdateAsync issues a single SQL UPDATE without materialising entities —
        // no domain events are dispatched (archival is intentionally a silent maintenance
        // operation, not a business change). Profiles with LastEnrichedAt == null are
        // excluded: we cannot reason about age without a timestamp. Take(batchSize) caps
        // the row count per round-trip so the transaction log stays bounded on the first
        // run after deployment.
        var candidates = _db.CompanyProfiles
            .Where(p => !p.IsArchived
                && p.TraceCount <= maxTraceCount
                && p.LastEnrichedAt != null
                && p.LastEnrichedAt < enrichedBefore)
            .OrderBy(p => p.LastEnrichedAt)
            .Take(batchSize);

        return await candidates
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.IsArchived, true),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CoverageByCountryRow>> GetCoverageByCountryAsync(
        DateTimeOffset now,
        int maxCountries,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCountries, nameof(maxCountries));

        // Single-pass group-by aggregate. We project sums + sample counts and compute
        // averages in the caller so profiles with null OverallConfidence / LastEnrichedAt
        // do not skew the means. DATEDIFF(DAY, LastEnrichedAt, now) returns int; we cast
        // to long before Sum() to avoid overflow on large datasets.
        var nowUtc = now.UtcDateTime;
        return await _db.CompanyProfiles
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .GroupBy(p => p.Country)
            .Select(g => new CoverageByCountryRow
            {
                Country = g.Key,
                ProfileCount = g.Count(),
                ConfidenceSampleCount = g.Count(p => EF.Property<double?>(p, "OverallConfidence") != null),
                ConfidenceSum = g.Sum(p => EF.Property<double?>(p, "OverallConfidence")) ?? 0d,
                EnrichedSampleCount = g.Count(p => p.LastEnrichedAt != null),
                EnrichedSumDays = g.Sum(p => p.LastEnrichedAt == null
                    ? 0L
                    : (long)EF.Functions.DateDiffDay(p.LastEnrichedAt.Value.UtcDateTime, nowUtc)),
            })
            .OrderByDescending(r => r.ProfileCount)
            .Take(maxCountries)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
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
