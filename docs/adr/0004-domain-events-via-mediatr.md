# ADR 0004 — Domain events via MediatR, dispatched in `TracerDbContext.SaveChangesAsync`

**Status.** Accepted (B-04 / B-05).

## Context

Several cross-cutting concerns react to specific Domain mutations:

- A `FieldChangedEvent` should fan out to SignalR + (Major+) Service Bus.
- A `CriticalChangeDetectedEvent` should publish to FieldForce, alert,
  and persist a high-severity audit record.
- A `ProfileCreatedEvent` should bump CKB metrics.

We need an in-process event mechanism that:
1. Doesn't reach into Infrastructure from Domain.
2. Fires **after** persistence — never on a half-persisted aggregate.
3. Lets handlers run synchronously inside the same SQL transaction
   when needed (audit), or asynchronously off-box (Service Bus).

## Decision

Domain events implement `IDomainEvent : MediatR.INotification`
(via `MediatR.Contracts` package referenced from `Tracer.Domain`).
Events are accumulated on `BaseEntity._domainEvents` while the aggregate
mutates, and dispatched by `TracerDbContext.SaveChangesAsync()`
**after** `base.SaveChangesAsync()` returns success.

Pseudocode:

```csharp
// TracerDbContext.SaveChangesAsync
var entitiesWithEvents = ChangeTracker.Entries<BaseEntity>()
    .Where(e => e.Entity.DomainEvents.Count > 0)
    .Select(e => e.Entity)
    .ToList();

var events = entitiesWithEvents.SelectMany(e => e.DomainEvents).ToList();
foreach (var entity in entitiesWithEvents) entity.ClearDomainEvents();

var result = await base.SaveChangesAsync(cancellationToken);
foreach (var @event in events) await _mediator.Publish(@event, cancellationToken);
return result;
```

Handlers MUST NOT call `SaveChangesAsync` themselves (recursive dispatch).
If a handler needs to persist (e.g. mark `IsNotified`), the **next**
caller's `SaveChangesAsync` flushes the change. This is exactly how
`CkbPersistenceService.PersistEnrichmentAsync` works: two saves, one for
domain-event dispatch, one for the `IsNotified` flag flush.

## Consequences

**Positive.**
- Domain layer stays infra-free; events declared in `Tracer.Domain`,
  handlers in `Tracer.Application/EventHandlers/`.
- Atomicity: if `SaveChangesAsync` throws, no events fire. No
  half-published Service Bus messages on rollback.
- Test ergonomics: `InMemoryUnitOfWork` mirrors the same dispatch
  semantics in unit / E2E tests (see B-77 harness).

**Negative.**
- Two-saves pattern is non-obvious. Tests that only call save once miss
  the `IsNotified` flush — documented in CLAUDE.md and the B-77 plan.
- `MediatR.Contracts` reference is the single non-BCL dependency in
  `Tracer.Domain`. Acceptable but worth flagging.

**Neutral.**
- `ChangeEvent.Id` is a `Guid` set in the constructor so handlers can
  correlate the event message to the database row immediately, without
  needing the post-save id.

## Related

- ADR 0001 — Clean Architecture.
- B-04 / B-05 introduced the `IDomainEvent` + dispatch pattern.
- B-32 Service Bus consumer + B-42 Change notifications use this directly.
- B-77 E2E test harness — `InMemoryUnitOfWork` must mirror dispatch.
- CLAUDE.md "Domain events" bullet.
