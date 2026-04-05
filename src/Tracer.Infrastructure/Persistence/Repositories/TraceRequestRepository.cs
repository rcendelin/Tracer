using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITraceRequestRepository"/>.
/// </summary>
internal sealed class TraceRequestRepository : ITraceRequestRepository
{
    private readonly TracerDbContext _db;

    public TraceRequestRepository(TracerDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(TraceRequest request, CancellationToken cancellationToken)
    {
        await _db.TraceRequests.AddAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TraceRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.TraceRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<TraceRequest>> ListAsync(
        int page, int pageSize,
        TraceStatus? status, DateTimeOffset? dateFrom, DateTimeOffset? dateTo,
        string? search, CancellationToken cancellationToken)
    {
        var query = ApplyFilters(_db.TraceRequests.AsNoTracking(), status, dateFrom, dateTo, search);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(
        TraceStatus? status, DateTimeOffset? dateFrom, DateTimeOffset? dateTo,
        string? search, CancellationToken cancellationToken)
    {
        var query = ApplyFilters(_db.TraceRequests.AsNoTracking(), status, dateFrom, dateTo, search);

        return await query.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IQueryable<TraceRequest> ApplyFilters(
        IQueryable<TraceRequest> query,
        TraceStatus? status, DateTimeOffset? dateFrom, DateTimeOffset? dateTo, string? search)
    {
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (dateFrom.HasValue)
            query = query.Where(r => r.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(r => r.CreatedAt <= dateTo.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r =>
                (r.CompanyName != null && r.CompanyName.Contains(term)) ||
                (r.RegistrationId != null && r.RegistrationId.Contains(term)));
        }

        return query;
    }
}
