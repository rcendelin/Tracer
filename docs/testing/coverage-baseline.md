# Test coverage baseline — B-91 snapshot (2026-04-24)

This document is a **static** snapshot of what the test projects cover today,
intended as the release-candidate baseline for B-92 (production deployment).
It is hand-maintained from the current branch state; rerun the counts below
when it drifts.

## How to reproduce the counts

```bash
# Source LOC
for p in Tracer.Domain Tracer.Application Tracer.Infrastructure Tracer.Api; do
  find src/$p -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l
  find src/$p -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec cat {} \; | wc -l
done

# Test files / [Fact]+[Theory] counts
for p in Tracer.Domain.Tests Tracer.Application.Tests Tracer.Infrastructure.Tests; do
  find tests/$p -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l
  grep -r '\[Fact\]\|\[Theory\]' tests/$p --include='*.cs' | wc -l
done
```

## Snapshot

| Project                    | Source files | Source LOC | Test files | Test methods |
|----------------------------|--------------|------------|------------|--------------|
| `Tracer.Domain`            | 34           | 1 846      | 6          | 80           |
| `Tracer.Application`       | 97           | 4 866      | 25         | 297          |
| `Tracer.Infrastructure`    | 87           | 9 776      | 33         | 375          |
| `Tracer.Api`               | 9            | 916        | —          | —            |
| **Total**                  | **227**      | **17 404** | **64**     | **752**      |

`Tracer.Api` endpoints are exercised indirectly through Minimal API endpoint
tests that live in `Tracer.Infrastructure.Tests/Integration/` (added in B-77).

## Covered well

- **Domain value objects** — `Confidence`, `Address`, `NormalizedKey`,
  `FieldTtl` have dedicated `Tracer.Domain.Tests/ValueObjects/` suites.
- **Providers** — every folder under `src/Tracer.Infrastructure/Providers/`
  has a matching test folder under `tests/Tracer.Infrastructure.Tests/Providers/`.
  WireMock + FakeHttpMessageHandler pattern is consistent.
- **Application services** — `CompanyNameNormalizer`, `FuzzyNameMatcher`,
  `LlmDisambiguator`, `GoldenRecordMerger`, `ConfidenceScorer`,
  `WaterfallOrchestrator`, `CkbPersistenceService`, `ChangeDetector`,
  `FieldTtlPolicy`, `GdprPolicy`, `RevalidationQueue`, `OffPeakWindow`,
  `EntityResolver` all have dedicated unit tests.
- **BackgroundService** — `RevalidationScheduler` has unit tests exercising
  manual queue drain, off-peak gate, per-profile timeout, and host-cancellation
  paths (uses the `Clock` + `DelayAsync` testing seams documented in `CLAUDE.md`).
- **Commands** — both `SubmitTrace` and `SubmitBatchTrace` have handler tests
  including FluentValidation coverage.
- **Messaging** — `ServiceBusPublisher`, `ServiceBusConsumer` pipeline,
  and the batch-endpoint publish pattern are tested.
- **Deep flow E2E** — `tests/Tracer.Infrastructure.Tests/Integration/`
  runs the full waterfall via `WebApplicationFactory<Program>` (B-77).

## Known gaps (triaged for follow-up blocks)

### Query handlers without a dedicated test fixture

Tests exist under `tests/Tracer.Application.Tests/Queries/` only for
`GetChangeStats`, `GetDashboardStats` (added in B-91), and `ListChanges`.
Handlers still without a direct unit test:

- `GetProfileHandler`
- `GetProfileHistoryHandler`
- `GetTraceResultHandler`
- `ListProfilesHandler`
- `ListTracesHandler`

Impact: low — these handlers are thin passthroughs over repositories, and the
projection DTOs are covered by `Tracer.Application/Mapping` extensions (which
are covered by handler tests that do use them, like `SubmitTrace`). Adding
direct handler fixtures is a worthwhile hygiene step; tracked as follow-up.

### Repository-level integration tests (shared follow-up with B-71, B-83, B-84)

`tests/Tracer.Infrastructure.Tests/` has **no** DbContext integration harness
yet. Every repository method below is only exercised indirectly through
higher-level tests that stub `ICompanyProfileRepository` /
`IChangeEventRepository` with NSubstitute:

| Repository                      | Untested method(s) requiring EF Core translation                                                   |
|---------------------------------|----------------------------------------------------------------------------------------------------|
| `CompanyProfileRepository`      | `ListAsync` (filters + pagination), `CountAsync`, **`GetAverageConfidenceAsync` (new in B-91)**,   |
|                                 | `ListByCountryAsync`, `GetRevalidationQueueAsync`, `ArchiveStaleAsync` (B-83),                     |
|                                 | `ListTopByTraceCountAsync` (B-79), `GetCoverageByCountryAsync` (B-84)                              |
| `ChangeEventRepository`         | `ListAsync`, `CountAsync`, `CountSinceAsync` (B-71), `GetMonthlyTrendAsync` (B-84)                 |
| `TraceRequestRepository`        | `ListAsync` + filter combinations, `CountAsync` (filter path for dashboard)                        |
| `ValidationRecordRepository`    | `CountSinceAsync` (B-71)                                                                            |
| `SourceResultRepository`        | all queries                                                                                         |

This is the **single most impactful follow-up** — it blocks verifying SQL
translation of EF Core LINQ queries (especially the shadow-property patterns
on `OverallConfidence`, JSON owned-type restrictions from `CLAUDE.md`, and
`ExecuteUpdateAsync` paths in archival). Recommended approach: add
Testcontainers.MsSql (already on Directory.Packages.props) and a
`DbContextFixture` under `tests/Tracer.Infrastructure.Tests/Persistence/`.

### Redis / cache

`CacheWarmingService` and the `StackExchangeRedisCache` DI branch from B-79
have no Testcontainers.Redis integration. `CLAUDE.md` already flags this as
a B-79 follow-up.

### Frontend tests

`src/Tracer.Web/` has no component tests. `DashboardPage`, `ProfilesPage`,
`ValidationDashboardPage`, and the `useSignalR` singleton hook are all
covered only by manual exploratory testing today. Adding Vitest + React
Testing Library is out of scope for B-91 — it is recommended as an
independent follow-up in Phase 4 polish.

## Live regression matrix (deferred to B-92)

B-91's nominal scope includes a 50 × 10 × 3 provider/country/depth matrix
against live APIs. That requires:

- All `V realizaci` feature blocks merged (B-66 / B-73 / B-78 / B-85 / B-90 at
  the time of this snapshot).
- Live keys for Companies House, Google Maps, Azure Maps, Azure OpenAI (kept
  in Key Vault).
- Deployed environment — the local sandbox has neither `dotnet` SDK nor
  inbound network to registry endpoints.

The matrix therefore moves to B-92 as a pre-cutover smoke test, driven by
the already-existing `deploy/scripts/smoke-test-phase2.sh` harness and the
k6 load scripts (B-86).

## Flaky / timing-sensitive candidates

Spot audit of test sources (no runtime data available in this sandbox):

- `RevalidationSchedulerTests` relies on a `Clock` seam and should be
  deterministic. No sleeps observed.
- `HandelsregisterClient` rate limiter tests use the `Clock` seam correctly —
  no wall-clock dependency.
- No test under `tests/**` uses `Task.Delay` without a linked CTS + timeout,
  and no test uses `Thread.Sleep` at all.

If CI develops flakes after merge, re-audit starting with
`tests/Tracer.Infrastructure.Tests/Providers/` — WireMock tests are the most
common source of timing issues in similar codebases.

## Action items (carry into B-92)

1. **Before production cutover:** run 50×10×3 live regression matrix via
   the Phase 2 smoke harness on a staging environment.
2. **First deployment cycle:** add Testcontainers.MsSql `DbContextFixture`
   and start filling the repository integration gap table above. Start with
   `CompanyProfileRepository.GetAverageConfidenceAsync` since it is new and
   touches the shadow-property path documented in `CLAUDE.md`.
3. **Phase 4 follow-up (new block):** Vitest + React Testing Library for the
   Tracer.Web SPA.
4. **Phase 4 follow-up (new block):** Testcontainers.Redis fixture + the
   `CacheWarmingService` integration test noted in B-79.

## Release-candidate signal

With the gaps above **documented and triaged**, the current `develop` head
is a valid release candidate for B-92 **at unit-test level**. Production
cutover must not proceed without the live regression matrix step (item 1
above), which B-92 will drive.
