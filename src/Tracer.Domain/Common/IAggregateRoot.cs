namespace Tracer.Domain.Common;

/// <summary>
/// Marker interface for aggregate roots in the domain model.
/// Only aggregate roots should be accessed directly via repositories.
/// Child entities are always accessed through their owning aggregate root.
/// </summary>
/// <remarks>
/// CA1040 (avoid empty interfaces) is suppressed intentionally.
/// The marker provides a compile-time constraint that enforces repository
/// access rules at the type system level without requiring inheritance.
/// </remarks>
#pragma warning disable CA1040 // Avoid empty interfaces
public interface IAggregateRoot;
#pragma warning restore CA1040
