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

- **CQRS via MediatR** — Commands mutate state, Queries read. All handlers in `Application/Commands/` and `Application/Queries/`.
- **Provider abstraction** — Every data source implements `IEnrichmentProvider`. Adding a new source = one new folder in `Infrastructure/Providers/` + DI registration.
- **Waterfall orchestrator** — Runs providers in priority order with fan-out/fan-in. Tier 1 (APIs) run in parallel, Tier 2 (scraping) sequentially, Tier 3 (AI) last.
- **TracedField<T>** — Every enriched field carries its own confidence score (0.0–1.0), source ID, and enrichment timestamp. This is the fundamental data unit.
- **Domain events** — Changes in CKB raise events (`FieldChangedEvent`, `CriticalChangeDetectedEvent`) that trigger notifications via Service Bus and SignalR.

## Solution structure

```
Tracer/
├── src/
│   ├── Tracer.Domain/           # Entities, Value Objects, Enums, Interfaces, Events
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
├── CLAUDE.md                    # This file
├── Tracer.sln
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
public record TracedField<T>
{
    public required T Value { get; init; }
    public required double Confidence { get; init; }     // 0.0–1.0
    public required string Source { get; init; }          // provider ID
    public required DateTimeOffset EnrichedAt { get; init; }
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

When a field value changes during enrichment or re-validation, a `ChangeEvent` is created with severity classification:

- **Critical** — entity dissolved/liquidated, insolvency
- **Major** — address change, officer change, name change
- **Minor** — phone/email/website change
- **Cosmetic** — confidence update, formatting change

Critical/Major changes trigger Service Bus notifications on topic `tracer-changes`.

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
- Topic `tracer-changes` — change event notifications

## Database

Azure SQL Serverless, EF Core Code-First migrations.

Tables: `TraceRequests`, `CompanyProfiles` (CKB), `SourceResults`, `ChangeEvents`, `ValidationRecords`.

CompanyProfiles stores enriched fields as JSON column (`FieldsJson`) mapped to EF Core owned types. Indexed on `NormalizedKey` (unique), `RegistrationId + Country`, and `LastValidatedAt` (for re-validation queue).

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
- Records for DTOs and Value Objects, classes for Entities
- Async suffix on all async methods
- Guard clauses, no nested ifs
- `IReadOnlyCollection<T>` for public collection properties
- Structured logging via `ILogger<T>` with `LoggerMessage.Define`
- ProblemDetails (RFC 7807) for all API error responses

## Git conventions

- `main` — production (protected)
- `develop` — integration
- Feature branches: `feature/TRACER-{n}-short-description`
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

## Implementation phases

**Phase 1 (MVP):** Sync API, ARES + GLEIF + Google Maps, basic CKB, Tracer UI (list + detail), deployment.
**Phase 2:** Service Bus, webhook, Companies House, ABN, SEC EDGAR, parallel waterfall, SignalR, cache, FieldForce integration, Change Detection.
**Phase 3:** Web scraping, AI extraction, Deep depth, entity resolution, re-validation engine, Change Feed, Validation Dashboard, GDPR layer.
**Phase 4:** Redis cache, monitoring, rate limiting, batch export, archival, trend analytics.
