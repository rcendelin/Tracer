namespace Tracer.Domain.Common;

public abstract class BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    // private set: allows EF Core to materialise the Id from the database via reflection-based
    // field/property access without requiring explicit field-access mode configuration.
    // Guid.NewGuid() provides a default for new entities created in code.
    public Guid Id { get; private set; } = Guid.NewGuid();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    // internal: only Infrastructure (SaveChangesAsync interceptor / UnitOfWork) should clear
    // domain events after they have been dispatched. Keeping this internal prevents Application
    // or API layers from silently dropping events before dispatch.
    internal void ClearDomainEvents() => _domainEvents.Clear();
}
