# B-71 — React — ValidationDashboard

**Phase:** 3 — AI + scraping
**Estimate:** 4 h
**Prerequisites:** B-65 (re-validation scheduler, `IRevalidationQueue`), B-68 (field TTL policy), existing Profiles/Changes/Trace surfaces.

## Goal

Expose the re-validation engine to operators through both API and UI:

- `GET /api/validation/stats` — aggregate counters for the re-validation engine.
- `GET /api/validation/queue` — paged list of CKB profiles that will be processed next.
- React **Validation Dashboard** page — cards with the counters, table of queued profiles, per-row "Revalidate now" button, live `ValidationProgress` updates via SignalR.

The block does **not** change the runner itself (B-66/B-67 own the lightweight / deep modes). This page is the read-side window into the scheduler and manual queue that B-65 delivered.

## Out of scope

- Lightweight/deep revalidation algorithms (B-66/B-67 own those).
- Per-field TTL tuning UI (operator-level config edit — belongs to B-72 overrides/admin area).
- Bulk "revalidate N profiles" button (single-profile is enough for Phase 3; queue is a bounded `Channel<Guid>` — bulk triggers would need B-66/B-67 to be meaningful).
- Dedicated validation metrics histogram UI (Phase 4 — Application Insights / Azure Monitor).

## Architectural decisions

### 1. Dedicated queries, not a reuse of existing ones

`ListProfiles` pre-orders by `TraceCount DESC, CreatedAt DESC` — irrelevant for an operator who wants to see "what the scheduler will pick next". The scheduler orders by `TraceCount DESC, LastValidatedAt ASC` (B-65), and only includes profiles that have **at least one expired field TTL** through `IFieldTtlPolicy` in B-68.

→ Add a query `ListValidationQueueQuery` that wraps `ICompanyProfileRepository.GetRevalidationQueueAsync` (already exists) + `IFieldTtlPolicy.GetExpiredFields(...)` to surface which fields will be re-checked. Pagination is client-side over the queue cap (keep scheduler semantics).

### 2. Stats aggregated in one handler, one repository round-trip per counter

`GetValidationStatsQuery` reads:

- **PendingCount** — total profiles with at least one expired field (`ICompanyProfileRepository.CountRevalidationQueueAsync`, new method — do not load profiles for counting).
- **ProcessedToday** — `COUNT(ValidationRecords WHERE ValidatedAt >= startOfDayUtc)` (new method on `IValidationRecordRepository`).
- **ChangesDetectedToday** — reuse `IChangeEventRepository.CountAsync(severity: null, profileId: null)` with an additional `detectedAfter: DateTimeOffset?` parameter (tiny extension to existing signature).
- **AverageDataAgeDays** — `AVG(DATEDIFF(day, LastValidatedAt, SYSUTCDATETIME()))` over non-archived profiles. Not a hot field; a dedicated repo method using `EF.Functions.DateDiffDay` keeps it SQL-side.

Handler awaits sequentially — **EF Core DbContext is not thread-safe** (CLAUDE.md). No `Task.WhenAll` across repositories.

### 3. Paging semantics

`Channel<Guid>` + the in-memory bounded cap mean the "queue" is not really paginated like a DB list. We still page the **scheduler sweep window** (`GetRevalidationQueueAsync(maxCount)`) to avoid loading thousands of profiles. Default `pageSize = 20`, cap 100 (consistent with the rest of the API). The server loads `(page+1) × pageSize` profiles, returns the slice, and flags `hasNextPage` if `Count == pageSize`. This is a pragmatic approximation of paging over a derived queue.

### 4. "Revalidate now" reuses the existing endpoint

`POST /api/profiles/{id}/revalidate` already enqueues into `IRevalidationQueue` (B-65). No new endpoint. Frontend calls the existing `profileApi.revalidate`.

### 5. SignalR: `ValidationProgress` is already fan-out-to-all

`useSignalR` already exposes `onValidationProgress`. Hook consumer pattern follows `useChangeFeedLiveUpdates` — factory callback is injected from `Layout` to avoid a second `useSignalR` subscriber. See CLAUDE.md "Frontend SignalR singleton".

### 6. No SignalR group changes

`ValidationProgress` fans out to all clients (same as `ChangeDetected`). Groups are only used for trace-scoped events. When B-66/B-67 emit `ValidationProgress`, the Validation page will pick them up globally.

## Subtasks

| # | Task | Files | Complexity |
|---|---|---|---|
| 1 | Add repository methods: `CompanyProfileRepository.CountRevalidationQueueAsync`, `ValidationRecordRepository.CountSince`, `IChangeEventRepository.CountAsync` extension with `detectedAfter`, `CompanyProfileRepository.AverageDaysSinceValidationAsync` | `src/Tracer.Domain/Interfaces/*.cs`, `src/Tracer.Infrastructure/Persistence/Repositories/*.cs` | S |
| 2 | Add `GetValidationStatsQuery` + `GetValidationStatsHandler` | `src/Tracer.Application/Queries/GetValidationStats/` | S |
| 3 | Add `ListValidationQueueQuery` + handler + DTO (`ValidationQueueItemDto` with `ProfileId`, `NormalizedKey`, `LegalName?`, `LastValidatedAt?`, `ExpiredFields: IReadOnlyCollection<FieldName>`, `NextFieldExpiryDate?`) | `src/Tracer.Application/Queries/ListValidationQueue/`, `src/Tracer.Application/DTOs/ValidationQueueItemDto.cs` | M |
| 4 | Add FluentValidation validators (page / pageSize) | same folder | S |
| 5 | Add `/api/validation` endpoint group: `GET /stats`, `GET /queue` | `src/Tracer.Api/Endpoints/ValidationEndpoints.cs`, `Program.cs` | S |
| 6 | Unit tests: handlers (NSubstitute repos), validator, repository smoke tests (existing Infrastructure.Tests pattern) | `tests/Tracer.Application.Tests/`, `tests/Tracer.Infrastructure.Tests/` | M |
| 7 | Frontend: extend `types/index.ts` with `ValidationStats`, `ValidationQueueItem`; extend `api/client.ts` with `validationApi`; add hook `useValidation.ts` with `useValidationStats`, `useValidationQueue`, `useValidationLiveUpdates` | `src/Tracer.Web/src/types/`, `src/Tracer.Web/src/api/`, `src/Tracer.Web/src/hooks/` | S |
| 8 | Frontend: `ValidationDashboardPage.tsx` — stat cards, queue table with expired-fields pills, "Revalidate" button (mutation), pagination, live updates | `src/Tracer.Web/src/pages/` | M |
| 9 | Frontend: register route `/validation`, add nav item, wire live updates from `Layout` | `src/Tracer.Web/src/main.tsx`, `src/Tracer.Web/src/components/Layout.tsx` | S |
| 10 | Tests for frontend hooks not in scope (no Vitest harness yet — confirmed); manual smoke via `npm run build` + existing lint | — | — |
| 11 | Update CLAUDE.md with conventions discovered (FluentValidation for validation queries, scheduler-queue paging approximation, `POST /.../revalidate` reuse) | `CLAUDE.md` | S |

Total complexity: S+S+M+S+S+M+S+M+S = roughly 4 h aligned with the estimate.

## Data model / API contracts

### `GET /api/validation/stats`

```json
{
  "pendingCount": 42,
  "processedToday": 12,
  "changesDetectedToday": 3,
  "averageDataAgeDays": 18.4
}
```

### `GET /api/validation/queue?page=0&pageSize=20`

```json
{
  "items": [
    {
      "profileId": "...",
      "normalizedKey": "CZ:00177041",
      "legalName": "ACME s.r.o.",
      "country": "CZ",
      "traceCount": 17,
      "lastValidatedAt": "2026-01-04T00:00:00Z",
      "expiredFields": ["EntityStatus", "Phone"],
      "nextFieldExpiryDate": "2025-12-20T00:00:00Z",
      "overallConfidence": 0.82
    }
  ],
  "page": 0,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

Both endpoints:

- Require `X-Api-Key` via existing middleware.
- Return enums as strings (existing `ConfigureHttpJsonOptions`).
- `pageSize` capped at 100.

No database migrations. No domain entity changes. No Service Bus contract changes.

## Testing strategy

**Unit (Application.Tests)**

- `GetValidationStatsHandler_ReturnsZeroes_WhenRepositoriesEmpty`
- `GetValidationStatsHandler_AggregatesFromRepositories` — NSubstitute stubs return known counts, handler produces expected DTO.
- `ListValidationQueueHandler_MapsExpiredFieldsViaPolicy` — sub `IFieldTtlPolicy` returns 2 expired fields; verify DTO carries them.
- `ListValidationQueueHandler_ClampsPageSize` — `pageSize=500` → repo called with 100.
- Validator tests for both queries (negative page → invalid, etc.).

**Unit (Infrastructure.Tests)**

- Repository smoke tests for the new count / average methods against in-memory SQLite (existing pattern).
- Edge: `AverageDaysSinceValidationAsync` returns 0 when no profiles exist.

**Integration** — none required beyond the existing API test harness for endpoint wiring (follows ProfileEndpoints pattern). One integration test per endpoint to verify 200 + JSON enum strings + auth.

**E2E / UI** — manual verification only (no Vitest harness in repo). Confirmed by `npm run build`.

## Acceptance criteria

1. `GET /api/validation/stats` returns 200 with the four counters; enums string-serialized.
2. `GET /api/validation/queue` returns 200, paged, with `expiredFields` populated correctly per `IFieldTtlPolicy`.
3. Frontend `/validation` route renders stat cards + queue table; hitting "Revalidate" returns 202 and the row refreshes.
4. `ValidationProgress` SignalR event invalidates the queue and stats queries (live refresh without polling).
5. Build green: `dotnet build`, `dotnet test` (affected projects), `npm run build` in `Tracer.Web`.
6. No new `npm audit` high/critical; no new NuGet vulnerabilities (`dotnet list package --vulnerable`).
7. Code review pass + security review pass (see main task steps 6 & 7).
8. CLAUDE.md updated with B-71 conventions.

## Risk & mitigation

- **JSON column LINQ trap (CLAUDE.md)** — `legalName` lives in a JSON column, so the queue DTO must read it in-memory after loading the profile (safe) rather than filtering on it. We do not expose search on the queue endpoint — ordered scheduler queue only.
- **Scoped repositories under one DbContext** — sequential awaits (no `Task.WhenAll`).
- **Stats and queue mutate over time** — client reacts by invalidating caches on `ValidationProgress` and on mutation responses, not by polling. TanStack `staleTime` kept at 15 s.
- **Rate limiting on revalidate** — existing 429 path already handled by `profileApi.revalidate`; frontend surfaces the queue-full toast and disables the button briefly.
