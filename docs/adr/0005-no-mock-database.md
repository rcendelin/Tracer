# ADR 0005 — Integration tests run against a real SQL Server (Testcontainers)

**Status.** Accepted (B-10 / B-49).

## Context

EF Core has well-documented gotchas when the in-memory provider is used
as a substitute for the real database:

- `ExecuteUpdateAsync` / `ExecuteDeleteAsync` aren't supported.
- JSON column queries (owned-type projections, `EF.Property<>`,
  `JsonValue` paths) translate differently or not at all.
- Filtered indexes, computed columns, datetime semantics, and case
  sensitivity diverge from SQL Server.

Several Tracer features rely on SQL Server-specific behaviour:

- `CompanyProfileConfiguration` declares `HasIndex(...).HasFilter("[IsArchived] = 0")`.
- `ArchivalService` issues bulk `UPDATE [CompanyProfiles] SET IsArchived = 1`
  via `ExecuteUpdateAsync(...).Take(batchSize)`.
- `OverallConfidence` aggregate uses
  `Select(p => EF.Property<double?>(p, "OverallConfidence")).AverageAsync()`
  — relies on `AVG(...)` ignoring NULLs.
- `GetMonthlyTrendAsync` group-bys on `e.DetectedAt.Year/.Month` translated
  to `DATEPART`.

Mocking these breaks the production guarantee.

## Decision

Integration tests run against a **real SQL Server** via
`Testcontainers.MsSql`. The container starts on test-class fixture set-up,
EF migrations run, then the test exercises real queries.

Where a SQL Server container would be over-kill (E2E HTTP tests focused
on application orchestration, not persistence) we use **in-memory fakes**
defined in `tests/Tracer.Infrastructure.Tests/Integration/Fakes/`. Those
fakes mirror the repository interfaces fully and **must** dispatch domain
events (see ADR 0004) and keep the two-saves contract.

We do **not** use `Microsoft.EntityFrameworkCore.InMemory` anywhere.

## Consequences

**Positive.**
- Provider-specific bugs (e.g. `DateDiffDay` overflow at 5.9 B day-rows,
  `AVG` on shadow property, filtered index activation) are caught by tests.
- Migrations are exercised on every CI run, not just at deploy time.
- The B-83 archival `ExecuteUpdateAsync` pattern was tested as it works
  in production, not as it would have worked in EF.InMemory.

**Negative.**
- CI is slower (Testcontainers starts + EF migrations ~ 25 s overhead).
  Mitigated by xUnit collection fixtures so the container is shared
  across tests in the same class.
- Local dev needs Docker. Documented in `CONTRIBUTING.md`.

**Neutral.**
- E2E tests (B-77) deliberately use in-memory fakes for the orchestration
  surface (`WaterfallOrchestrator`, `EntityResolver`, `CkbPersistenceService`)
  while the **application services** stay real. The fakes carry the same
  domain-event semantics so the test exercise is meaningful.

## Related

- ADR 0001 — Clean Architecture.
- ADR 0004 — Domain events.
- B-10 EF Core DbContext setup.
- B-49 Phase 2 provider integration tests.
- B-77 E2E harness with in-memory repos for orchestration tests.
- CLAUDE.md "EF Core JSON column LINQ queries" + "Filtered descending index" bullets.
