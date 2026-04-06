using MediatR;
using Microsoft.EntityFrameworkCore;
using Tracer.Domain.Common;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the Tracer application.
/// Implements <see cref="IUnitOfWork"/> with automatic domain event dispatch
/// after successful persistence.
/// </summary>
public sealed class TracerDbContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;

    public TracerDbContext(DbContextOptions<TracerDbContext> options, IMediator mediator)
        : base(options)
    {
        _mediator = mediator;
    }

    public DbSet<TraceRequest> TraceRequests => Set<TraceRequest>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<SourceResult> SourceResults => Set<SourceResult>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<ValidationRecord> ValidationRecords => Set<ValidationRecord>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect domain events from all tracked aggregate roots before saving.
        // Events are snapshotted first — saving may trigger further changes.
        var domainEvents = CollectDomainEvents();

        var result = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Dispatch after successful save so handlers see committed state.
        await DispatchDomainEventsAsync(domainEvents, cancellationToken).ConfigureAwait(false);

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TracerDbContext).Assembly);
    }

    private List<IDomainEvent> CollectDomainEvents()
    {
        var entities = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var events = entities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        foreach (var entity in entities)
            entity.ClearDomainEvents();

        return events;
    }

    private async Task DispatchDomainEventsAsync(
        List<IDomainEvent> domainEvents,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
