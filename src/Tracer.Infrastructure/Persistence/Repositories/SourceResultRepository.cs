using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISourceResultRepository"/>.
/// </summary>
internal sealed class SourceResultRepository : ISourceResultRepository
{
    private readonly TracerDbContext _db;

    public SourceResultRepository(TracerDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(SourceResult result, CancellationToken cancellationToken)
    {
        await _db.SourceResults.AddAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SourceResult>> ListByTraceRequestAsync(
        Guid traceRequestId, CancellationToken cancellationToken)
    {
        return await _db.SourceResults
            .AsNoTracking()
            .Where(r => r.TraceRequestId == traceRequestId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
