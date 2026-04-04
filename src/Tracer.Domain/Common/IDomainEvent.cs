namespace Tracer.Domain.Common;

/// <summary>
/// Marker interface for domain events. Implementations are raised by aggregate roots
/// and handled via MediatR INotification handlers in the Application layer.
/// </summary>
/// <remarks>
/// CA1040 (avoid empty interfaces) is suppressed intentionally.
/// Marker interfaces are an established DDD pattern used here to provide
/// strong typing and discoverability without requiring a shared base class.
/// </remarks>
#pragma warning disable CA1040 // Avoid empty interfaces
public interface IDomainEvent;
#pragma warning restore CA1040
