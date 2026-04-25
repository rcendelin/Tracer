using MediatR;
using Tracer.Domain.Common;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// In-memory <see cref="IUnitOfWork"/> that mirrors <c>TracerDbContext.SaveChangesAsync</c> by
/// collecting domain events from tracked aggregate roots (via their repositories) and
/// dispatching them through <see cref="IMediator"/> after "save".
/// </summary>
/// <remarks>
/// Keeping this behaviour in tests is essential — the CKB persistence pipeline relies on
/// domain event dispatch for change notifications (<c>FieldChangedNotificationHandler</c>,
/// <c>CriticalChangeNotificationHandler</c>). A no-op UnitOfWork would silently drop those events.
/// </remarks>
internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly InMemoryCompanyProfileRepository _profileRepo;
    private readonly InMemoryTraceRequestRepository _traceRepo;
    private readonly InMemoryChangeEventRepository _changeRepo;
    private readonly IMediator _mediator;

    public InMemoryUnitOfWork(
        InMemoryCompanyProfileRepository profileRepo,
        InMemoryTraceRequestRepository traceRepo,
        InMemoryChangeEventRepository changeRepo,
        IMediator mediator)
    {
        _profileRepo = profileRepo;
        _traceRepo = traceRepo;
        _changeRepo = changeRepo;
        _mediator = mediator;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot first — dispatching handlers may queue new events that belong to the next tick.
        var entities = new List<BaseEntity>();
        entities.AddRange(_profileRepo.All.Cast<BaseEntity>());
        entities.AddRange(_traceRepo.All.Cast<BaseEntity>());
        entities.AddRange(_changeRepo.All.Cast<BaseEntity>());

        var events = entities
            .Where(e => e.DomainEvents.Count > 0)
            .SelectMany(e => e.DomainEvents.ToArray())
            .ToList();

        foreach (var entity in entities.Where(e => e.DomainEvents.Count > 0))
            entity.ClearDomainEvents();

        foreach (var domainEvent in events)
            await _mediator.Publish(domainEvent, cancellationToken).ConfigureAwait(false);

        return events.Count;
    }
}
