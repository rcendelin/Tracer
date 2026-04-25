# Tracer — architecture

## 1. Mission

Tracer turns sparse company hints (name, phone, address, registration ID,
industry hint) into a comprehensive, confidence-scored company profile by
querying free public data sources, scraping registry websites, and falling
back on a constrained LLM extraction step.

The enriched profiles are persisted in the **Company Knowledge Base (CKB)**.
Every trace either creates a new CKB entry or updates an existing one;
field-level changes flow into a `ChangeEvent` audit log and downstream
notifications (FieldForce / monitoring) via Service Bus and SignalR.

## 2. Layers

```
┌────────────────────────────────────────────────────────────────────┐
│ Tracer.Api          ASP.NET Core Minimal API + SignalR hub         │
│  └─ Endpoints/      …(/api/trace, /api/profiles, /api/changes,…)   │
│  └─ Middleware/     ApiKey, SecurityHeaders, RateLimiter            │
│  └─ OpenApi/        TracerOpenApiDocumentTransformer                │
├────────────────────────────────────────────────────────────────────┤
│ Tracer.Application  Use cases, MediatR handlers, services           │
│  └─ Commands/       SubmitTrace, OverrideField, AcknowledgeChange,…│
│  └─ Queries/        GetProfile, ListChanges, GetChangeStats,…       │
│  └─ Services/       WaterfallOrchestrator, CkbPersistenceService,…  │
│  └─ Services/Export  CsvInjectionSanitizer, exporters               │
├────────────────────────────────────────────────────────────────────┤
│ Tracer.Domain       Entities, Value Objects, Interfaces             │
│  └─ Entities/       CompanyProfile, ChangeEvent, TraceRequest, …    │
│  └─ ValueObjects/   TracedField<T>, Confidence, FieldTtl, …         │
│  └─ Interfaces/     IEnrichmentProvider, ICompanyProfileRepository  │
├────────────────────────────────────────────────────────────────────┤
│ Tracer.Infrastructure  EF Core, providers, Service Bus, OpenAI       │
│  └─ Persistence/    TracerDbContext, repositories, configurations   │
│  └─ Providers/      ARES, GLEIF, CompaniesHouse, AbnLookup, SEC,    │
│                     GoogleMaps, AzureMaps, Handelsregister, StateSos│
│                     BrazilCnpj, LATAM, WebScraper, AiExtractor      │
│  └─ Caching/        DistributedCacheRegistration (InMemory ↔ Redis) │
│  └─ BackgroundJobs/ RevalidationScheduler, ArchivalService,         │
│                     CacheWarmingService                             │
│  └─ Telemetry/      TracerMetrics                                   │
├────────────────────────────────────────────────────────────────────┤
│ Tracer.Web          React 19 + Vite SPA (separate Static Web App)   │
│  └─ pages/          Dashboard, Traces, Profiles, ChangeFeed,        │
│                     ValidationDashboard                             │
│  └─ hooks/          useSignalR (singleton), TanStack Query wrappers │
│  └─ components/     Skeleton, ErrorMessage, EmptyState, Toast       │
├────────────────────────────────────────────────────────────────────┤
│ Tracer.Contracts    Service Bus message contracts (NuGet)           │
└────────────────────────────────────────────────────────────────────┘
```

Dependency rule: arrows point inward only. `Tracer.Domain` knows nothing
of EF Core, ASP.NET, MediatR or HTTP; `Tracer.Application` depends only
on Domain; `Tracer.Infrastructure` implements the Domain interfaces and
references third-party SDKs; `Tracer.Api` wires everything together.

`Tracer.Contracts` is intentionally an isolated package, multi-targeting
`net8.0` and `net10.0`, with zero external dependencies. FieldForce
references *only* this package.

## 3. Hot path — what happens during a trace

```
HTTP POST /api/trace
  │  X-Api-Key check (ApiKeyAuthMiddleware)
  │  RateLimiter check (Polly)
  ▼
SubmitTraceCommand → SubmitTraceHandler
  │  1. EntityResolver: hash + fuzzy match → existing CompanyProfile?
  │  2. WaterfallOrchestrator:
  │       Tier 1 (registry APIs)  parallel via Task.WhenAll, ≤8s/provider
  │       Tier 2 (registry scrape) sequential, ≤12s/provider
  │       Tier 3 (AI extractor)    sequential (Deep only), ≤20s
  │     Total budget: Quick 5s / Standard 15s / Deep 30s
  │  3. GoldenRecordMerger: per-field confidence tournament
  │  4. ConfidenceScorer: overall score
  │  5. CkbPersistenceService.PersistEnrichmentAsync:
  │       - Auto-unarchive if needed
  │       - For each field: CompanyProfile.UpdateField → ChangeEvent
  │       - SaveChangesAsync (1) → dispatch domain events
  │       - SaveChangesAsync (2) → flush IsNotified flags
  ▼
TraceResultDto + 201 Created
```

Critical rules baked in:

- **EF Core DbContext is not thread-safe** — orchestrator runs Tier 1
  providers in parallel, but providers MUST NOT touch the DbContext or
  any repository. CKB context is passed through `TraceContext.ExistingProfile`.
- **`Critical` change → Service Bus + SignalR**. `Major` → SignalR only.
  `Minor` → log only (UI polls). `Cosmetic` → log only, never published.
- **Field TTL** is checked on every trace via `IFieldTtlPolicy`. Expired
  fields are eligible for re-enrichment in the same waterfall pass.

## 4. Read path — UI / FieldForce

| Endpoint | Returns | Cached? |
|---|---|---|
| `GET /api/profiles` | Paginated `CompanyProfile`s | TanStack 30 s |
| `GET /api/profiles/{id}` | Profile detail | TanStack 30 s, IDistributedCache |
| `GET /api/profiles/{id}/history` | `ChangeEvent` list | — |
| `GET /api/changes` | Paginated changes (since-filter) | — |
| `GET /api/changes/stats` | Severity counts | TanStack 30 s |
| `GET /api/validation/stats` | Re-validation KPI | TanStack 30 s |
| `GET /api/validation/queue` | Pending profiles | TanStack 30 s |
| `GET /api/analytics/changes?period=Monthly` | Monthly buckets | TanStack 5 min |
| `GET /api/analytics/coverage?groupBy=Country` | Per-country aggregate | TanStack 5 min |
| `GET /api/profiles/export` | CSV / XLSX (≤ 10 000 rows) | n/a |
| `GET /api/changes/export` | CSV / XLSX (≤ 10 000 rows) | n/a |

`IDistributedCache` is `InMemory` by default; production opts in to Redis
via `Cache:Provider = Redis` + `ConnectionStrings:Redis`. See
[configuration.md](./configuration.md).

## 5. Background services

| Service | Schedule | Effect |
|---|---|---|
| `RevalidationScheduler` | Hourly tick + on-demand queue | Re-runs waterfall for profiles with expired TTLs |
| `ArchivalService` | Daily | Bulk-archives stale, low-traffic profiles via `UPDATE IsArchived = 1` |
| `CacheWarmingService` | Once at startup (opt-in) | Pre-loads top-N profiles into IDistributedCache |
| `PersonalDataRetentionService` | Daily | GDPR Art. 17: erases personal-data fields after retention window |

All Singleton `BackgroundService`s; for Scoped EF-Core repositories they
create `IServiceScopeFactory.CreateAsyncScope()` per unit of work.
See ADR [0004 — Domain events via MediatR](./adr/0004-domain-events-via-mediatr.md).

## 6. Observability

- **Structured logs:** Serilog → console + Azure Monitor (App Insights).
  Every log line is enriched with `TraceId` / `SpanId` from the active
  OpenTelemetry Activity.
- **Metrics:** `ITracerMetrics` (`tracer.*` Meter). Counters and histograms
  for waterfall stages, re-validation runs, rate-limit drops, archival,
  CKB unarchives, cache hit ratio.
- **Tracing:** `Activity` source via OpenTelemetry; `WithTracing(...)` is
  always registered (so dev `TraceId` propagation works), `UseAzureMonitor`
  only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured.
- **Health:** `/health` (Swagger-discoverable JSON) returns SQL probe
  + Redis probe (Degraded only) + `self`.
- **Alerts:** Azure Monitor scheduled queries (response-time, error-rate,
  re-validation failures). See `deploy/bicep/modules/monitoring.bicep`.

## 7. Security envelope

- API-key via `X-Api-Key` header (also tolerated as `Authorization: Bearer`
  for SignalR WebSocket). Keys are validated at startup
  (`ApiKeyOptionsValidator`) — too short / duplicate / expired → fail to boot.
- HSTS in production (`UseHsts()` only when `IsProduction()`).
- Security headers middleware: CSP, X-Content-Type-Options, X-Frame-Options,
  Referrer-Policy, Permissions-Policy, COOP, CORP. See
  [adr/0001-clean-architecture.md](./adr/0001-clean-architecture.md) and
  source: `src/Tracer.Api/Middleware/SecurityHeadersMiddleware.cs`.
- GDPR: `IGdprPolicy` classifies fields as `PersonalData | Firmographic`;
  `WaterfallOrchestrator` strips personal-data fields when
  `TraceRequest.IncludeOfficers = false`. `IPersonalDataAccessAudit`
  logs every personal-data read.
- SSRF guard on every server-side HTTP client that takes user-supplied
  URLs (private-IP DNS resolution check + `AllowAutoRedirect = false`).
- CSV export sanitises every cell that could be interpreted as a formula
  (`= + - @`, TAB, CR) — see `CsvInjectionSanitizer`.

## 8. Where to look next

- New provider integrations → [providers.md](./providers.md)
- Why the architecture is shaped this way → [adr/](./adr/)
- I broke something at 03:00 → [operations/troubleshooting.md](./operations/troubleshooting.md)
- "What does this knob do?" → [configuration.md](./configuration.md)
