# ADR 0001 — Clean Architecture, four projects, dependencies inward

**Status.** Accepted (B-01).

## Context

Tracer is a long-lived service with multiple integration boundaries
(REST, Service Bus, SignalR, Azure SQL, Azure OpenAI, half a dozen
public registries). Without an explicit dependency rule, "wiring up the
SDK" tends to drift into the domain logic, and the project becomes hard
to test or refactor.

## Decision

Adopt Clean Architecture with four C# projects and a strict inward
dependency rule:

```
Tracer.Api          ──depends on──▶  Tracer.Application + Tracer.Infrastructure
Tracer.Application  ──depends on──▶  Tracer.Domain
Tracer.Infrastructure ─depends on──▶  Tracer.Domain (implements interfaces)
Tracer.Domain       ──depends on──▶  (nothing except BCL + MediatR.Contracts)
```

A separate **`Tracer.Contracts`** package multi-targets `net8.0` + `net10.0`
and exposes only the Service Bus message types that FieldForce consumes.
Zero external dependencies.

A separate **`Tracer.Web`** React 19 SPA lives in its own folder and is
shipped as an Azure Static Web App; the API serves no HTML.

## Consequences

**Positive.**
- Domain code is unit-testable without ASP.NET, EF Core, or any HTTP stack.
- Swapping infrastructure (e.g. Cosmos for SQL, RabbitMQ for Service Bus)
  changes only `Tracer.Infrastructure`.
- FieldForce takes a `Tracer.Contracts` NuGet package and never sees Domain types.

**Negative.**
- Manual mapping between Domain entities and Application DTOs (no Mapster).
  Mitigated by static extension methods in `Application/Mapping/`.
- New developers must internalise the rule; PR reviews enforce it.
- Some friction: e.g. `Confidence` value object lives in Domain but is
  exposed through a shadow EF property (see ADR 0002 / B-91).

**Neutral.**
- `MediatR.Contracts` is the only NuGet `Tracer.Domain` references — needed
  so domain events can implement `INotification`. This is documented and
  accepted; the rest of MediatR sits in `Tracer.Application`.

## Related

- ADR 0002 — `TracedField<T>` as the unit of enrichment data.
- ADR 0004 — Domain events via MediatR + `TracerDbContext.SaveChangesAsync`.
- `CLAUDE.md` "Architecture" section.
