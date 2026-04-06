# Tracer — Company Data Enrichment Engine

## Project overview

Tracer is a standalone .NET 10 microservice that enriches partial company information (name, phone, address, registration ID, industry hint) into comprehensive company profiles using free public data sources, web scraping, and AI extraction. It sits alongside FieldForce (CRM for industrial/agricultural sector) and communicates via REST API (sync) and Azure Service Bus (async).

Tracer internally builds a **Company Knowledge Base (CKB)** — a persistent database of enriched company profiles that grows with every query, validates data cyclically, and monitors changes.

## Tech stack

- **Runtime:** .NET 10, C# 13
- **Web:** ASP.NET Core Minimal API
- **Frontend:** React 19, Vite 6, TanStack Query v5, SignalR client
- **ORM:** Entity Framework Core 10 (Code-First)
- **CQRS:** MediatR 12
- **Validation:** FluentValidation 11
- **HTTP Resilience:** Microsoft.Extensions.Http.Resilience (Polly v8)
- **Messaging:** Azure.Messaging.ServiceBus
- **AI:** Azure.AI.OpenAI (GPT-4o-mini, structured outputs)
- **HTML Parsing:** AngleSharp
- **Database:** Azure SQL Serverless
- **Cache:** IDistributedCache (in-memory → Redis in Phase 4)
- **Geocoding:** Azure Maps
- **Places:** Google Maps Places API (New)
- **Testing:** xUnit, NSubstitute, FluentAssertions, WireMock.Net, Testcontainers

## Architecture

Clean Architecture with four layers. Dependencies flow inward only.

```
Tracer.Api (entry point, Minimal API endpoints, SignalR hubs)
  └── Tracer.Application (use cases, MediatR handlers, orchestrator)
      └── Tracer.Domain (entities, value objects, interfaces, events)
Tracer.Infrastructure (EF Core, provider adapters, Service Bus, OpenAI, caching)
  └── implements interfaces from Domain
Tracer.Web (React 19 SPA, separate project, served via Static Web App)
```

### Key design patterns

- **CQRS via MediatR** — Commands mutate state, Queries read. All handlers in `Application/Commands/` and `Application/Queries/`. ValidationBehavior runs FluentValidation before handlers. DI via `services.AddApplication()`.
- **Provider abstraction** — Every data source implements `IEnrichmentProvider`. Adding a new source = one new folder in `Infrastructure/Providers/` + DI registration. HTTP clients use `Microsoft.Extensions.Http.Resilience` with Polly. `HttpClient.Timeout = Infinite` — Polly controls all timeouts.
- **Waterfall orchestrator** — Runs providers in priority order with fan-out/fan-in. Tier 1 (APIs) run in parallel, Tier 2 (scraping) sequentially, Tier 3 (AI) last.
- **TracedField<T>** — Every enriched field carries its own confidence score (0.0–1.0), source ID, and enrichment timestamp. This is the fundamental data unit.
- **Domain events** — `IDomainEvent : MediatR.INotification` (via `MediatR.Contracts` in Domain). Events raised by aggregate roots are collected in `BaseEntity._domainEvents` and dispatched by `TracerDbContext.SaveChangesAsync()` after successful persistence. Handlers must NOT call `SaveChangesAsync` (recursive dispatch). `FieldChangedEvent` and `CriticalChangeDetectedEvent` carry `ChangeEventId` for correlation. Critical → Service Bus + SignalR; Major → SignalR; Minor/Cosmetic → logged only.
- **Repository abstraction** — `ICompanyProfileRepository`, `ITraceRequestRepository`, `IChangeEventRepository`, `IValidationRecordRepository` + `IUnitOfWork`. All in `Domain/Interfaces/`. EF Core implementations in `Infrastructure/Persistence/Repositories/`. DI via `services.AddInfrastructure(connectionString)`. Confidence filtering uses `EF.Property<double?>` for SQL translation.
- **Application Services** — Orchestration services in `Application/Services/`: `WaterfallOrchestrator` (provider pipeline), `GoldenRecordMerger` (field conflict resolution), `ConfidenceScorer` (scoring), `EntityResolver` (dedup matching), `ChangeDetector` (field-level change detection with severity classification), `CkbPersistenceService` (CKB upsert coordination). Each has an interface + implementation, registered via `services.AddApplication()`.
- **Pagination** — All list handlers clamp page (≥0) and pageSize (1–100). Zero-based page index. `PagedResult<T>` wrapper for API responses.

## Solution structure

```
Tracer/
├── src/
│   ├── Tracer.Contracts/        # Shared NuGet package — Service Bus message contracts for FieldForce
│   ├── Tracer.Domain/           # Entities, Value Objects, Enums, Events, Interfaces
│   ├── Tracer.Application/      # Commands, Queries, Services, EventHandlers, DTOs
│   ├── Tracer.Infrastructure/   # Persistence, Providers, Messaging, BackgroundJobs, Caching
│   ├── Tracer.Api/              # Endpoints, SignalR Hubs, Middleware, Program.cs
│   └── Tracer.Web/              # React 19 SPA (Vite)
├── tests/
│   ├── Tracer.Domain.Tests/
│   ├── Tracer.Application.Tests/
│   └── Tracer.Infrastructure.Tests/
├── deploy/
│   ├── bicep/                   # Azure IaC
│   └── github/workflows/       # CI/CD
├── docs/
│   └── integration/             # FieldForce consumer skeleton and integration guides
├── CLAUDE.md                    # This file
├── Tracer.slnx
├── .editorconfig
├── Directory.Build.props
└── Directory.Packages.props     # Central Package Management
```

## Core domain concepts

### CompanyProfile (Aggregate Root)

The golden record in CKB. One profile = one unique company. Identified by `RegistrationId + Country` or a normalized name hash.

Key fields: LegalName, TradeName, RegistrationId, TaxId, LegalForm, RegisteredAddress, OperatingAddress, Phone, Email, Website, Industry, EmployeeRange, EntityStatus, ParentCompany, Location. Each is a `TracedField<T>`.

CKB metadata: CreatedAt, LastEnrichedAt, LastValidatedAt, TraceCount, OverallConfidence, IsArchived.

Methods: `UpdateField()` returns a `ChangeEvent` if value changed. `NeedsRevalidation()` checks TTL policy. `IncrementTraceCount()` tracks usage for priority scoring.

### TracedField<T> (Value Object)

```csharp
public sealed record TracedField<T>
{
    public required T Value { get; init; }
    public required Confidence Confidence { get; init; }  // Confidence VO wrapping [0.0–1.0]
    public required string Source { get; init; }           // provider ID, e.g. "ares"
    public required DateTimeOffset EnrichedAt { get; init; }
    public bool IsExpired(TimeSpan ttl) => DateTimeOffset.UtcNow - EnrichedAt > ttl;
}
```

### IEnrichmentProvider (Interface)

```csharp
public interface IEnrichmentProvider
{
    string ProviderId { get; }           // "ares", "companies-house", "gleif-lei"
    int Priority { get; }               // lower = higher priority (10=registry, 250=AI)
    double SourceQuality { get; }       // 0.0–1.0 for confidence scoring
    bool CanHandle(TraceContext context);
    Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken ct);
}
```

### Provider priority tiers

| Priority | Tier | Providers |
|----------|------|-----------|
| 10–20    | 1 (Registry API) | ARES, Companies House, ABN Lookup, SEC EDGAR |
| 30       | 1 (Global)       | GLEIF LEI |
| 50       | 1 (Geo)          | Google Maps Places, Azure Maps |
| 150      | 2 (Scraping)     | Web Scraper (company website via AngleSharp) |
| 200      | 2 (Registry scrape) | Handelsregister, CNPJ, State SoS |
| 250      | 3 (AI)           | AI Extractor (Azure OpenAI structured output) |

### TraceDepth

- `Quick` — CKB cache + fastest APIs only, target <5s
- `Standard` — Full waterfall (Tier 1 + 2), target <10s
- `Deep` — Standard + web scraping + AI extraction, target <30s

### Change detection

`IChangeDetector` service compares newly enriched fields against existing profile state, applies changes via `CompanyProfile.UpdateField()`, and returns a `ChangeDetectionResult` with all detected `ChangeEvent`s. Severity classification:

- **Critical** — entity dissolved/liquidated, insolvency
- **Major** — address change, officer change, name change
- **Minor** — phone/email/website change
- **Cosmetic** — confidence update, formatting change

Critical changes → `CriticalChangeNotificationHandler` publishes to Service Bus topic `tracer-changes` + SignalR `ChangeDetected`. Major changes → `FieldChangedNotificationHandler` pushes SignalR only. `CkbPersistenceService` marks all `ChangeEvent`s as notified after domain event dispatch completes.

## API endpoints

### Trace (core enrichment)

```
POST   /api/trace              Submit enrichment request, returns TraceResult
GET    /api/trace/{traceId}    Get trace status and results
POST   /api/trace/batch        Submit batch (array of requests), returns TraceId[]
```

### Profiles (CKB)

```
GET    /api/profiles                      List profiles (paged, filterable)
GET    /api/profiles/{id}                 Get profile detail
GET    /api/profiles/{id}/history         Get change history for profile
POST   /api/profiles/{id}/revalidate     Trigger manual re-validation
PUT    /api/profiles/{id}/fields/{field}  Manual field override (with audit)
```

### Changes

```
GET    /api/changes            List change events (paged, filterable by severity)
GET    /api/changes/stats      Aggregated change statistics
```

### Validation

```
GET    /api/validation/stats   Re-validation engine statistics
GET    /api/validation/queue   Profiles pending validation
```

### SignalR Hub: `/hubs/trace`

Events: `SourceCompleted`, `TraceCompleted`, `ChangeDetected`, `ValidationProgress`

### Service Bus

- Queue `tracer-request` — inbound enrichment requests
- Queue `tracer-response` — outbound enrichment results
- Topic `tracer-changes` — change event notifications (default subscription: Critical + Major only)

Message contracts are defined in `Tracer.Contracts` (standalone NuGet, zero external dependencies, multi-targets net8.0+net10.0). FieldForce references only this package. Application uses `global using` aliases from `Application/Messaging/TraceRequestMessage.cs`. `ContractMappingExtensions` maps between Application DTOs and Contracts types using `MapEnum<TSource,TTarget>` with `Enum.IsDefined` validation to catch enum drift at runtime.

## Database

Azure SQL Serverless, EF Core Code-First migrations.

Tables: `TraceRequests`, `CompanyProfiles` (CKB), `SourceResults`, `ChangeEvents`, `ValidationRecords`.

CompanyProfiles stores enriched fields as JSON columns via EF Core `ToJson()` owned types. `TracerDbContext` implements `IUnitOfWork`. Indexed on `NormalizedKey` (unique), `RegistrationId + Country`, and `LastValidatedAt` (filtered WHERE IsArchived=0, SQL Server-only). FK relationships with `DeleteBehavior.Restrict` on all child entities. `Confidence` VO uses explicit `ValueConverter<Confidence?, double?>`. Entity configurations in `Infrastructure/Persistence/Configurations/`.

## Data sources (free, no commercial APIs)

| Source | Region | Type | Key data |
|--------|--------|------|----------|
| ARES | CZ/SK | REST API, free | IČO, name, legal form, address, VAT |
| Companies House | UK | REST API, free (600 req/5min) | CRN, SIC, officers, PSC, filings |
| SEC EDGAR | US (public) | REST API, free (10 req/s) | CIK, filings, XBRL financials |
| ABN Lookup | Australia | SOAP/JSON, free | ABN, entity type, legal name, GST |
| GLEIF LEI | Global | REST API, free, CC0 | Legal name, address, parent chain |
| Google Maps Places | Global | REST API ($200/mo free) | Address, phone, website, GPS |
| Azure Maps | Global | REST API (5K/day free) | Batch geocoding |
| Web Scraper | Global | AngleSharp + HttpClient | Structured data from company websites |
| AI Extractor | Global | Azure OpenAI GPT-4o-mini | Structured extraction from unstructured text |

## Re-validation engine

Background service (`RevalidationScheduler`) runs hourly. Selects profiles from CKB where fields have expired TTLs, prioritized by business importance (TraceCount) and risk (recent Critical changes).

**Field TTL defaults:**
- EntityStatus: 30 days
- Officers: 90 days
- Phone/Email: 180 days
- RegisteredAddress: 365 days
- RegistrationId/TaxId: 730 days

Two modes: Lightweight (re-check only expired fields against primary registry) and Deep (full waterfall re-enrichment). Daily budget: 50–100 profiles.

## Coding conventions

- C# 13, file-scoped namespaces, nullable reference types enabled globally
- `sealed` by default on all classes that are not designed for inheritance
- Records for DTOs and Value Objects, classes for Entities. DTOs are sealed records in `Application/DTOs/`
- Manual mapping via static extension methods in `Application/Mapping/` (no Mapster — explicit, zero-dependency)
- Entities extend `BaseEntity` (Id + domain events), aggregate roots also implement `IAggregateRoot`
- Private parameterless constructor on entities for EF Core materialisation
- State transitions via explicit methods with guard clauses (throw `InvalidOperationException` on invalid state)
- Domain events are sealed records implementing `IDomainEvent`; carry only IDs and enums, never PII
- Change detection via JSON comparison in `CompanyProfile.UpdateField()` — compare before mutating state
- Unbounded string fields (error messages, raw responses) are truncated at domain level (2KB/50KB)
- Provider error messages sanitized — no raw `ex.Message` in `ProviderResult.Error()`, only generic strings
- `OperationCanceledException` in providers: use `when (!cancellationToken.IsCancellationRequested)` for Timeout, let caller cancellation propagate
- Providers registered as Transient (typed HttpClient factory manages handler lifetime; Singleton would cause captive dependency)
- Stateless application services (`IConfidenceScorer`, `IGoldenRecordMerger`, `IChangeDetector`) registered as Singleton. Scoped for services with repository dependencies (`IWaterfallOrchestrator`, `ICkbPersistenceService`, `IEntityResolver`).
- API keys validated at startup in DI registration (throw if missing)
- Search queries capped at max length before sending to external APIs
- API key auth via `X-Api-Key` header middleware (`ApiKeyAuthMiddleware`). Dev: no keys = pass-through. Production: throws if unconfigured.
- PageSize capped at 100 on all API list endpoints
- Async suffix on all async methods
- Guard clauses, no nested ifs — state guards before argument guards in mutation methods
- `IReadOnlyCollection<T>` for public collection properties
- Structured logging via `ILogger<T>` with `LoggerMessage.Define`
- ProblemDetails (RFC 7807) for all API error responses
- **NormalizedKey format:** `{CountryCode}:{RegistrationId}` (colon separator, e.g. `"CZ:00177041"`). Stable identifier — survives company renames.
- **Enum mirroring rule** — `Tracer.Contracts.Enums` mirrors `Tracer.Domain.Enums` by integer value. When adding a Domain enum member, add the matching member to Contracts with the same int value. `MapEnum<,>` in `ContractMappingExtensions` throws `InvalidOperationException` at runtime if values drift.
- Dead-letter error descriptions use exception type name only (no `ex.Message`) — raw messages can expose internal paths/connection strings (CWE-209). Full exception is always in structured logs.

## Git conventions

- `main` — production (protected)
- `develop` — integration
- Feature branches: `feature/TRACER-B{nn}-short-description` (block-based, e.g. `feature/TRACER-B04-domain-entity-trace-request`)
- Conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`
- Squash merge to develop, merge commit to main

## Build and run

```bash
# Backend
dotnet restore
dotnet build
dotnet test

# Run API (requires connection strings in appsettings.Development.json or user-secrets)
cd src/Tracer.Api
dotnet run

# Frontend
cd src/Tracer.Web
npm install
npm run dev
```

## Environment variables / configuration

All secrets via Azure Key Vault or user-secrets in development. Key configuration sections:

- `ConnectionStrings:TracerDb` — Azure SQL connection string
- `ConnectionStrings:ServiceBus` — Service Bus connection string
- `Providers:CompaniesHouse:ApiKey` — Companies House API key
- `Providers:GoogleMaps:ApiKey` — Google Maps API key
- `Providers:AzureOpenAI:Endpoint` — Azure OpenAI endpoint
- `Revalidation:Enabled` — toggle re-validation engine (default: true)
- `Revalidation:IntervalMinutes` — scheduler interval (default: 60)
- `Revalidation:MaxProfilesPerRun` — batch size (default: 100)

## Azure — Přísný zákaz destruktivních operací

**NIKDY** bez explicitního pokynu uživatele nespouštět žádné příkazy (`az`, `azd`, `terraform`, `bicep`, PowerShell Az modul ani jiné CLI/SDK), které:

- mění konfiguraci existujících Azure komponent (App Service, SQL, Service Bus, Key Vault, Static Web App, Storage, Application Insights, apod.)
- zakládají nové Azure komponenty nebo resource groupy
- mažou nebo archivují Azure komponenty nebo resource groupy
- mění přístupová práva, role nebo síťová pravidla (RBAC, firewall, VNet)
- rotují nebo mažou secrets, connection stringy nebo API klíče

Toto platí i pro operace, které vypadají jako "read-only", ale mají side effect (např. `az webapp restart`, `az sql db import`).

Pokud je Azure akce nezbytná, **navrhni příkaz uživateli a počkej na výslovné schválení**.

## Implementation phases

**Phase 1 (MVP):** Sync API, ARES + GLEIF + Google Maps, basic CKB, Tracer UI (list + detail), deployment.
**Phase 2:** Service Bus, webhook, Companies House, ABN, SEC EDGAR, parallel waterfall, SignalR, cache, FieldForce integration, Change Detection.
**Phase 3:** Web scraping, AI extraction, Deep depth, entity resolution, re-validation engine, Change Feed, Validation Dashboard, GDPR layer.
**Phase 4:** Redis cache, monitoring, rate limiting, batch export, archival, trend analytics.
