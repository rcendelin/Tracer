using MediatR;

namespace Tracer.Domain.Common;

/// <summary>
/// Marker interface for domain events. Extends <see cref="INotification"/>
/// so that events raised by aggregate roots are automatically dispatched
/// via MediatR after <see cref="IUnitOfWork.SaveChangesAsync"/> completes.
/// </summary>
#pragma warning disable CA1040 // Avoid empty interfaces — intentional DDD marker extending MediatR
public interface IDomainEvent : INotification;
#pragma warning restore CA1040
