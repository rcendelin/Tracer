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
- **Waterfall orchestrator** — Runs providers in priority order with fan-out/fan-in. Tier 1 (APIs, priority ≤ 100) run in parallel via `Task.WhenAll`; Tier 2 (scraping, priority 101–200) sequential; Tier 3 (AI, priority > 200) sequential. Depth gates: Tier 1 runs for all depths; Tier 2 requires Standard or Deep; Tier 3 requires exactly Deep. Total depth budget: Quick=5s, Standard=15s, Deep=30s — on expiry partial results are used (no throw). Per-tier provider timeouts: Tier1=8s, Tier2=12s, Tier3=20s.
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
POST   /api/trace              Submit enrichment request, returns TraceResult (201 Created)
GET    /api/trace/{traceId}    Get trace status and results
POST   /api/trace/batch        Submit batch (≤200 items), returns BatchTraceResultDto (202 Accepted)
```

**Batch endpoint semantics:** Persist-all-then-publish pattern — all `TraceRequest` entities are saved in one transaction (`TraceStatus.Queued = 6`), then each is published to the `tracer-request` Service Bus queue (best-effort). Items that fail to publish return `TraceStatus.Failed` in the response so the caller can identify them. Items stuck in `Queued` status (publish failed, DB persisted) are candidates for future reconciliation. Caller can supply `CorrelationId` per item; it is echoed back in `BatchTraceItemDto` for request-reply matching.

Rate limited: 5 requests/minute per IP (`"batch"` policy, `FixedWindow`, `QueueLimit = 0` → immediate 429, no queuing).

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
  - Subscription `fieldforce-changes` — SQL filter `Severity='Critical' OR Severity='Major'` (default FieldForce feed)
  - Subscription `monitoring-changes` — implicit `1=1` TrueFilter (receives every published severity; Cosmetic is log-only and never published)

**Severity → publish matrix** (authoritative — `FieldChangedNotificationHandler` + `CriticalChangeNotificationHandler`):

| Severity | Service Bus | SignalR | Log | fieldforce-changes | monitoring-changes |
|---|---|---|---|---|---|
| Critical | ✅ (Critical handler) | ✅ | ✅ | ✅ | ✅ |
| Major | ✅ (Field handler) | ✅ | ✅ | ✅ | ✅ |
| Minor | ✅ (Field handler) | ❌ (UI polls) | ✅ | ❌ (filter drops) | ✅ |
| Cosmetic | ❌ | ❌ | ✅ | — | — |

Both subscriptions set `deadLetteringOnFilterEvaluationExceptions: true` so a broken rule parks the message in DLQ instead of silently dropping it. `monitoring-changes` has `maxDeliveryCount: 10` vs fieldforce's 5 to tolerate transient consumer outages.

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
| BrasilAPI CNPJ | BR | REST API, free, no key | CNPJ, legal name, trade name, address, CNAE, status, phone, email |
| State SoS | US (CA/DE/NY) | HTML scraping, free (20 req/min) | Filing number, legal name, entity status, entity type |
| Handelsregister | DE | HTML scraping, free (60 req/h) | HRB/HRA number, legal name, address, legal form, officers |
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
- **Middleware order constraint** — `app.UseApiKeyAuth()` must come before `app.UseRateLimiter()`. If reversed, unauthenticated requests consume rate limit slots before being rejected.
- **ForwardedHeaders required for rate limiting** — behind Azure App Service / Front Door, `RemoteIpAddress` is the proxy IP without `app.UseForwardedHeaders()`. Configure `KnownIPNetworks` with RFC 1918 ranges; use `System.Net.IPNetwork.Parse("10.0.0.0/8")` (.NET 10 API — the old `Microsoft.AspNetCore.HttpOverrides.IPNetwork` is deprecated).
- **`TraceStatus.Queued = 6`** — added for batch submissions waiting in Service Bus queue. `MarkQueued()` transitions from `Pending`. `MarkInProgress()` accepts both `Pending` and `Queued` (Service Bus consumer picks up Queued items).
- **SignalR API key auth** — WebSocket upgrade cannot send custom headers. `ApiKeyAuthMiddleware.ExtractApiKey()` checks three sources in order: (1) `X-Api-Key` header, (2) `Authorization: Bearer <key>`, (3) `access_token` query string. The frontend `useSignalR.ts` uses `accessTokenFactory` to inject the key as Bearer, which SignalR converts to the `access_token` query param during WebSocket negotiation.
- **SignalR hub groups** — `SourceCompleted` and `TraceCompleted` are sent to `Clients.Group(traceId)`, not `Clients.All`. Clients must call hub method `SubscribeToTrace(traceId)` to join the group before receiving trace-specific events. `ChangeDetected` is sent to `Clients.All` (global monitoring concern). When adding new hub events, decide group vs. all explicitly.
- **Frontend SignalR singleton** — `src/Tracer.Web/src/hooks/useSignalR.ts` uses a module-level singleton `HubConnection` + `consumerCount` reference counter so all React components share one WebSocket. Do NOT duplicate this hook or create a second `HubConnectionBuilder` instance — it will silently open a second connection. Hooks that need SignalR events (e.g. `useChangeFeedLiveUpdates`) must accept the `on*` callback factory as a parameter rather than calling `useSignalR()` internally — a second call registers competing lifecycle handlers on the singleton and breaks reconnection state tracking.
- **EF Core DbContext is not thread-safe** — never use `Task.WhenAll` over multiple repository calls within the same HTTP request scope (scoped `DbContext` is shared across all repositories in the scope). Use sequential `await` for multiple queries in one handler. CA1849 on `.Result` after `WhenAll` is a symptom of the same root cause — the fix is sequential queries, not a wrapper helper.
- **EF Core JSON column LINQ queries** — `p.OwnedField.Value.Contains(search)` compiles but throws at runtime because EF Core cannot translate JSON path + `Contains` to SQL for owned types mapped via `ToJson()`. Safe search: filter only on regular columns (e.g. `RegistrationId`, `NormalizedKey`). Name search via JSON requires a separate computed/denormalized column.
- **FluentValidation on Queries** — `ValidationBehavior` runs for all MediatR requests, not just Commands. Add `AbstractValidator<TQuery>` for any query that accepts user-supplied parameters (e.g. `ListProfilesValidator` validates `Search` length, `Country` format, confidence range). Pattern: same folder as the query (`Queries/{Name}/{Name}Validator.cs`).
- **Enum JSON serialization** — Enums serialize as **strings** via `JsonStringEnumConverter` registered in `Program.cs` via `builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))`. Use `ConfigureHttpJsonOptions`, NOT `builder.Services.AddJsonOptions` — Minimal API ignores the MVC options object. Without this, all enum fields (e.g. `ChangeSeverity`, `FieldName`, `TraceStatus`) silently serialize as integers.
- **NSubstitute + internal types in tests** — `Substitute.For<ILogger<T>>()` throws `ArgumentException` when `T` is `internal` and the logger assembly is strong-named (`Microsoft.Extensions.Logging.Abstractions`). Castle.DynamicProxy cannot generate the proxy. Use `NullLogger<T>.Instance` instead. Affects all `internal sealed` provider/client classes in `Tracer.Infrastructure`.
- **`InternalsVisibleTo("DynamicProxyGenAssembly2")` pro NSubstitute internal interfacey** — `InternalsVisibleTo("Tracer.Infrastructure.Tests")` nestačí pro NSubstitute mock `internal` interfaceů (např. `IWebScraperClient`, `ISecEdgarClient`). Castle.DynamicProxy generuje proxy v separátní assembly `DynamicProxyGenAssembly2` — bez druhé InternalsVisibleTo položky v `.csproj` hází `InvalidProxyConstructorArgumentsException`. Obě položky musí být v `Tracer.Infrastructure.csproj`. Neplatí pro silně podepsané assembly (viz výše pro ILogger).
- **WireMock for absolute-URL providers** — Providers that construct absolute URLs (SEC EDGAR: `https://efts.sec.gov/...`, `https://data.sec.gov/...`) ignore the WireMock `BaseAddress`. Use a `FakeHttpMessageHandler : HttpMessageHandler` that pattern-matches `request.RequestUri.Host` instead of WireMock. `WithParam(key, value)` in WireMock compares against URL-decoded parameter values — pass the decoded string (e.g. `"BHP Group Limited"`, not `"BHP%20Group%20Limited"`).
- **`WebApplicationFactory<Program>` — bez subclassu** — `Program` je `internal partial class`; vytvoření podtřídy `WebApplicationFactory<Program>` způsobí `CS9338` (méně přístupný než nadtřída). Řešení: `new WebApplicationFactory<Program>().WithWebHostBuilder(builder => { ... })` přímo v každém testu — žádný subclass. Metodu pro vytvoření factory označit `static` (CA1822).
- **Polly `SamplingDuration` v test hostu** — `WebhookCallbackService` nastavuje `AttemptTimeout = 30s`. Výchozí `SamplingDuration = 30s` porušuje pravidlo `>= 2 × AttemptTimeout` → `OptionsValidationException` při startu `WebApplicationFactory`. Fix v `ConfigureTestServices`: `services.ConfigureAll<HttpStandardResilienceOptions>(o => o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2))`.
- **Auth v integration testech** — `appsettings.Development.json` obsahuje dev API klíč; `ApiKeyAuthMiddleware` ho validuje i v test hostu. Nutné obojí: (1) `["Auth:ApiKeys:0"] = "test-api-key"` v `WithWebHostBuilder` konfiguraci, (2) `client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key")` na každý client.
- **Deserializace enum odpovědí v testech** — API registruje `JsonStringEnumConverter` přes `ConfigureHttpJsonOptions` (ne `AddJsonOptions`). `response.Content.ReadFromJsonAsync<T>()` bez options selže nebo vrátí `0` pro enum hodnoty. Vždy předat `new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }`.
- **Enum namespace aliasy (Contracts vs Domain)** — `Tracer.Contracts.Enums` i `Tracer.Domain.Enums` deklarují `TraceStatus` a `TraceDepth`. V testech pokrývajících obě vrstvy nutné aliasy: `using ContractsEnums = Tracer.Contracts.Enums; using DomainEnums = Tracer.Domain.Enums;`.
- **`Confidence.Create(double)`** — factory metoda hodnoty objektu `Confidence` je `.Create(double value)`, nikoli `.From()`.
- **`UseAzureMonitor()` je podmíněné** — `Program.cs` volá `UseAzureMonitor()` pouze pokud je nastavené `APPLICATIONINSIGHTS_CONNECTION_STRING` env var nebo `AzureMonitor:ConnectionString` v konfiguraci. Bez toho by `WebApplicationFactory` padla při startu s `OptionsValidationException` (SamplingDuration validation). `.WithMetrics(AddMeter(ITracerMetrics.MeterName))` a `.WithTracing(AddAspNetCoreInstrumentation)` jsou registrovány vždy pro lokální observabilitu.
- **`ITracerMetrics.MeterName` jako interface const** — `TracerMetrics` je `internal sealed`; `Program.cs` přistupuje k názvu meteru přes `ITracerMetrics.MeterName` (const na interface = public). Nikdy nepřistupuj přímo na `TracerMetrics.MeterNameValue` z Api projektu.
- **`IServiceScopeFactory` v health checks** — health checks jsou registrovány jako Singleton (`AddCheck<T>()`), ale `TracerDbContext` je Scoped. Přímá injekce DbContextu způsobuje captive dependency. `DatabaseHealthCheck` používá `IServiceScopeFactory` a vytváří scope per invokaci. Platí obecně: pokud `IHealthCheck` implementace potřebuje Scoped dependency, vždy inject `IServiceScopeFactory`. Registrace přes `builder.Services.AddHealthChecks().AddInfrastructureHealthChecks()` — extension method v `InfrastructureServiceRegistration.cs`.
- **`UnsafeRelaxedJsonEscaping` v ChangeEvent JSON** — `CompanyProfile.UpdateField()` serializuje `PreviousValueJson`/`NewValueJson` s `UnsafeRelaxedJsonEscaping` (zachovává `+`, `<`, `>` jako-je). Tato data jsou exponována přes `GET /api/changes`, `GET /api/profiles/{id}/history` a SignalR. Bezpečné jen v React JSX kontextu (auto-escape). Nikdy nerenderuj přes `innerHTML` nebo jiný unsafe mechanismus (CWE-79).
- **SSRF protection v server-side HTTP klientech** — `WebScraperClient` blokuje před každým requestem URL, která se resolvuje na privátní/rezervovanou IP (RFC 1918, loopback, 169.254.x.x IMDS, CGNAT 100.64.x.x). Pattern: `IsBlockedUrlAsync(uri, DnsResolve, ct)` volán po validaci URL, před `http.GetAsync`. `AllowAutoRedirect = false` v `ConfigurePrimaryHttpMessageHandler` zabraňuje bypass přes 302 redirect na interní host. Platí pro všechny budoucí HTTP klienty přijímající URL od uživatele/externího zdroje.
- **`AddStandardResilienceHandler` — `MaxRetryAttempts` musí být ≥ 1** — Polly validator odmítne hodnotu 0 s `OptionsValidationException` při startu (a `WebApplicationFactory` to zachytí jako selhání testu). Pro "bez retry" nastav `MaxRetryAttempts = 1` — `TotalRequestTimeout` stejně nedovolí dokončit retry. Platí pro všechny registrace v `InfrastructureServiceRegistration.cs`.
- **DNS resolver jako testovací seam v `WebScraperClient`** — `DnsResolve { get; init; }` (`Func<string, CancellationToken, Task<IPAddress[]>>`) je `internal` property s defaultem `Dns.GetHostAddressesAsync`. V unit testech nastav stub vracející veřejnou IP (`1.1.1.1`), jinak `IsBlockedUrlAsync` provede reálné DNS dotazy na fake hostname a zablokuje testy. Vzor: `new WebScraperClient(...) { DnsResolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1") }) }`.
- **`AiExtractorProvider.CanHandle` používá přesné `Depth == Deep`** — na rozdíl od ostatních providerů (které používají `>= Standard` nebo `>= Deep`) vyžaduje AI provider přesně Deep. Záměrné: B-58 přestukturuje waterfall tiery; změna na `>=` by spustila AI extrakci na Standard trasách a narušila cíl ≤10s latency.
- **`SendAsync` jako testovací seam v `AiExtractorClient`** — `SendAsync { get; init; }` (`Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<string>>`) je `internal` property s defaultem volajícím `ChatClient.CompleteChatAsync`. V unit testech nastav stub vracející fake JSON: `new AiExtractorClient(fakeAzureClient, ...) { SendAsync = (_, _, _) => Task.FromResult(json) }`. `AzureOpenAIClient` v testech lze vytvořit s fake URI — constructor neprovádí síťové volání.
- **`WaterfallOrchestrator.DepthTimeoutOverride`** — `internal Func<TraceDepth, TimeSpan>? { get; init; }` testovací seam. Stejný vzor jako `DnsResolve` v `WebScraperClient` a `SendAsync` v `AiExtractorClient`. V testech nastav `DepthTimeoutOverride = _ => TimeSpan.FromMilliseconds(150)` aby se nemuselo čekat na reálné 5–30s depth budgety.
- **`OperationCanceledException` three-way discrimination v `ExecuteProviderWithTimeoutAsync`** — Správný pattern: (1) `when (!cancellationToken.IsCancellationRequested && timeoutCts.Token.IsCancellationRequested)` = pouze per-provider timeout → vrať `ProviderResult.Timeout`; (2) jakýkoliv jiný OCE = depth budget nebo caller cancellation → re-throw. Outer `ExecuteAsync` catch rozlišuje depth budget (`effectiveCt.IsCancellationRequested && !originalCt.IsCancellationRequested`) od caller cancel. Pozor: `timeoutCts` je linked k `effectiveCt` → když depth budget vyprší, oba tokeny jsou cancelled → podmínka (1) je false → správně re-throw.
- **`IEnrichmentProvider` DbContext constraint** — Tier 1 providery běží přes `Task.WhenAll` (paralelně). Implementace nesmí přistupovat k `DbContext` ani žádnému repository — EF Core DbContext není thread-safe. Veškerá CKB data musí být předána přes `TraceContext.ExistingProfile` / `TraceContext.AccumulatedFields`. Porušení způsobuje nedeterministické EF Core concurrency exception v produkci.
- **Handelsregister rate limiting (60 req/h)** — `HandelsregisterClient` enforces German Data Usage Act §9 via a sliding-window `ConcurrentQueue<DateTimeOffset>` + `SemaphoreSlim`. Limit is per-instance; horizontal scaling requires distributed rate limiting (Redis). `Clock` property is injectable for testing. Do NOT increase the 60 req/h constant — it's a legal obligation.
- **Registry scraper provider pattern (B-59)** — Tier 2 registry scrapers follow `WebScraperProvider` structure: `IClient` interface (search + detail), `Client` impl (AngleSharp HTML parsing, SSRF guard, rate limiting), `Provider` wrapper (CanHandle by country + regex, EnrichAsync with Stopwatch + timeout discrimination). For new registry scrapers (CNPJ, State SoS), clone the Handelsregister folder structure. Key: always return `ProviderResult.Error("generic message")` (CWE-209), log exception type only (`ex.GetType().Name`), not full stack.
- **German status normalization** — `HandelsregisterProvider.NormalizeStatus()` maps German status strings to canonical English: `aktiv→active`, `gelöscht→dissolved`, `aufgelöst→in_liquidation`, `insolvent→insolvent`. Uses `OrdinalIgnoreCase` comparison (not `ToLowerInvariant`) to satisfy CA1308. Same pattern should apply to future non-English registry providers.
- **BrasilAPI CNPJ provider (B-60)** — `BrazilCnpjProvider` uses BrasilAPI (`brasilapi.com.br/api/cnpj/v1/{cnpj}`) — free REST JSON API, no key required. CNPJ-only lookup (no name search). Follows ARES pattern (not Handelsregister scraping pattern). `BrazilCnpjClient.NormalizeCnpj()` strips formatting chars (`.`, `-`, `/`) to 14 digits. `FormatCnpj()` converts back to `XX.XXX.XXX/XXXX-XX`. Portuguese status normalization: `ATIVA→active`, `BAIXADA→dissolved`, `SUSPENSA→suspended`, `INAPTA→inactive`, `NULA→annulled`.
- **CNPJ format regex** — `^\d{14}$` for normalized CNPJ. Formatted: `^\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}$`. `BrazilCnpjClient.NormalizeCnpj()` handles both. `CanHandle()` normalizes before matching.
- **Brazilian phone formatting** — BrasilAPI returns concatenated DDD+number (e.g. `"2132242164"`). `FormatBrazilPhone()` converts to `+55 (21) 3224-2164`. Handles both 10-digit (landline) and 11-digit (mobile) numbers.
- **US State SoS Strategy pattern (B-61)** — `StateSosProvider` uses a Strategy pattern: `IStateSosAdapter` interface per state (CA, DE, NY), `StateSosClient` dispatches to matching adapter(s). Adapters are registered as Singletons (stateless HTML parsers). To add a new state: create `Adapters/{State}Adapter.cs` implementing `IStateSosAdapter` + register in DI. `CanHandle()` checks `!AccumulatedFields.Contains(FieldName.RegistrationId)` to skip when SEC EDGAR (Priority 20) already enriched. RegistrationId format: `{state}:{filingNumber}` (e.g. `CA:C0806592`). Rate limit: 20 req/min (shared across states). US status normalization: `Active/Good Standing→active`, `Dissolved/Cancelled/Revoked→dissolved`, `Suspended/Forfeited→suspended`, `Merged/Converted→merged`.
- **CompanyNameNormalizer (B-62)** — Extracted from `EntityResolver.NormalizeName()` into standalone `ICompanyNameNormalizer` service (Singleton, thread-safe). 8-step pipeline: uppercase → transliterate (ß→SS, Ø→O, full Cyrillic→Latin) → remove diacritics (FormD) → remove punctuation → expand abbreviations (INTL→INTERNATIONAL, etc.) → remove stopwords (7 languages) → remove legal form tokens (200+ patterns, 30+ countries) → remove single-letter remnants → sort tokens. **Key design:** token-based matching (not regex) for legal forms/stopwords — prevents false matches inside words (e.g. "DA" inside "SKODA"). `EntityResolver` delegates to `ICompanyNameNormalizer`.
- **Fuzzy name matching (B-63)** — `IFuzzyNameMatcher` (Singleton, stateless) produces a combined similarity score `0.6 × JaroWinkler + 0.4 × TokenJaccard` on pre-normalized names. Callers must normalize via `ICompanyNameNormalizer` upstream — the matcher does NOT normalize. `EntityResolver.ResolveAsync` uses it as a fallback after exact hash match fails: loads up to 100 candidates via `ICompanyProfileRepository.ListByCountryAsync`, scores each, auto-matches at `≥ 0.85`. `EntityResolver.FindCandidatesAsync(input, maxCandidates, minScore, ct)` exposes mid-tier candidates (0.70–0.85) for downstream LLM disambiguation (B-64). **Threshold semantics:** single-token typos (e.g. "MICROSOFT" vs "MIKROSOFT") get Jaccard=0 so the combined score is capped at ~0.6 — this is by design; exact-hash match (via the normalizer) handles single-token duplicates. Fuzzy is primarily for multi-token name variants.
- **`ListByCountryAsync` repository method** — `ICompanyProfileRepository.ListByCountryAsync(country, maxCount, ct)` returns non-archived profiles for a country, ordered by `TraceCount DESC` so business-important profiles are preferred as fuzzy candidates. Caller must cap `maxCount` (EntityResolver uses 100). `EntityResolver.ScoreCandidatesAsync` adds a defensive in-memory `Take(MaxFuzzyCandidates)` as DoS guard against future repository regressions.
- **LLM disambiguation (B-64)** — `ILlmDisambiguator` (public interface, internal `LlmDisambiguator` Scoped impl) picks the best match for ambiguous fuzzy candidates (0.70 ≤ score < 0.85) using Azure OpenAI GPT-4o-mini. Client split: `ILlmDisambiguatorClient` interface in Application (internal, used by `LlmDisambiguator`), `LlmDisambiguatorClient` impl in Infrastructure (mirrors `AiExtractorClient` pattern: `SendAsync` Func testability seam, strict JSON schema output, UTF-8 aware truncation). `NullLlmDisambiguatorClient` is the Application default so the app boots without Azure OpenAI; Infrastructure registration overrides when `Providers:AzureOpenAI:Endpoint` is set (falls back to shared endpoint/key or separate `DisambiguatorDeploymentName`/`DisambiguatorMaxTokens` keys). **Calibration:** `calibrated = rawConfidence × 0.7`; match threshold `≥ 0.5`. `index = -1` means "no match" (LLM-initiated); any other negative index is rejected defensively. Confidence clamped to `[0, 1]` with drift-warning log. Max 5 candidates passed (Application cap), Infrastructure enforces 10 as defense-in-depth. Prompt truncated at 16 KB with warning log.
- **`InternalsVisibleTo("DynamicProxyGenAssembly2")` in Tracer.Application** — Required in `Tracer.Application.csproj` so NSubstitute (via Castle.DynamicProxy) can mock `internal` interfaces like `ILlmDisambiguatorClient`. Also `InternalsVisibleTo Tracer.Infrastructure` lets the Infrastructure layer implement the internal client interface; `InternalsVisibleTo Tracer.Infrastructure.Tests` lets infra tests construct internal Application DTOs (`DisambiguationRequest`) directly. Same pattern applies to future Azure-backed services in Application.
- **GDPR field classification (B-69)** — `FieldClassification` enum (Domain) + `IGdprPolicy` (Application, Singleton) are the single source of truth for whether a `FieldName` is personal data. **Classification map is intentionally hard-coded in `GdprPolicy.Classify()` — adding a new personal-data field is an architectural change, never a config toggle.** Currently only `FieldName.Officers` is `PersonalData`; all other fields default to `Firmographic`. `IsPersonalData(field)` and `RequiresConsent(field)` are kept as separate predicates even though they are currently equivalent, so a future legal basis (legitimate interest vs. consent) can be expressed without a breaking API change. Retention (`PersonalDataRetention`) reads from `Gdpr:PersonalDataRetentionDays`, default 1095 d (≈36 months). Options are registered via `AddOptions<GdprOptions>().Validate(...).ValidateOnStart()` so misconfig fails at boot, not at first resolve.
- **Personal-data access audit (B-69)** — `IPersonalDataAccessAudit` (Singleton) records reads of personal-data fields for GDPR Art. 30 compliance. Default impl `LoggingPersonalDataAccessAudit` writes a structured log entry (EventId 9001) per read; suppressed when `Gdpr:AuditPersonalDataAccess=false` (dev only — production must keep auditing on). Caller must pass a non-empty `accessor` and `purpose`; guard clauses throw even when auditing is disabled so callers cannot silently pass invalid data. **Security note for B-70+:** `accessor` must be derived server-side (API key name / principal), never taken from the request body — the audit log is only as trustworthy as the caller identity feeding it. PII itself (officer names) is **never** written to the audit log — only the fact of access.
- **Re-validation scheduler (B-65)** — `RevalidationScheduler` is a Singleton `BackgroundService` in `Tracer.Infrastructure/BackgroundJobs/`. Abstractions (`IRevalidationRunner`, `IRevalidationQueue`, `RevalidationOutcome`, `OffPeakWindow`, `RevalidationOptions`) live in `Tracer.Application/Services/` — scheduler is the only Infrastructure-level piece. Registration: options are always bound so the API can inspect them; the hosted service itself is only registered when `Revalidation:Enabled = true` in `Program.cs`. `NoOpRevalidationRunner` is the Application-default `IRevalidationRunner` until B-66/B-67 replace it. The runner contract forbids calling `SaveChangesAsync` itself — persistence is coordinated by the scheduler.
- **BackgroundService + Scoped deps — `IServiceScopeFactory.CreateAsyncScope()` per unit of work** — `RevalidationScheduler` is Singleton but every processed profile needs Scoped `TracerDbContext` (EF Core). Create one `AsyncServiceScope` per profile (manual queue) or per repository call (auto sweep); never hold the scope across profiles. Same pattern as `ServiceBusConsumer` and `DatabaseHealthCheck`. Violating this causes captive-dependency cross-thread EF Core exceptions in production.
- **Three-tier cancellation in `RevalidationScheduler`** — (1) host `stoppingToken` cancels the outer loop; (2) per-profile linked CTS with `PerProfileTimeout = 5 min` bounds a single runner invocation; (3) `OperationCanceledException` handler uses `when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)` to discriminate per-profile timeout (→ `RevalidationOutcome.Failed` + log) from host cancellation (→ re-throw). Any other exception is caught, logged as `ex.GetType().Name` only (CWE-209 mitigation — `ex.Message` can leak paths/connection strings), and the tick continues to the next profile.
- **Off-peak gate applies only to the automatic sweep** — `OffPeakWindow.IsWithin(now)` returns `true` when disabled, or when UTC hour falls inside `[StartHourUtc, EndHourUtc)`. Wrap-around windows (`22 → 6`) supported (`hour >= Start || hour < End`); equal start/end means empty window (always outside). `RevalidationScheduler.RunTickAsync` drains the manual queue FIRST (every tick, unconditionally), then short-circuits on the off-peak gate before calling `ICompanyProfileRepository.GetRevalidationQueueAsync`. User-initiated revalidation must not block on quiet hours.
- **`IRevalidationQueue` is a bounded `Channel<Guid>`** — `RevalidationQueue` wraps `Channel.CreateBounded<Guid>(Capacity = 100, FullMode = DropWrite, SingleReader = true)`. `POST /api/profiles/{id}/revalidate` returns HTTP 429 (`ProblemDetails`) when full — do NOT block or enlarge the queue in-process; durable / distributed queueing is a Phase 4 concern (Redis). `TryEnqueueAsync` throws `ArgumentException` on `Guid.Empty` so bugs in the API layer surface loudly. Queue is registered Singleton in `Application/DependencyInjection.cs`.
- **Revalidation metrics — `ITracerMetrics.RecordRevalidationRun(trigger, processed, skipped, failed, durationMs)`** — Emits `tracer.revalidation.duration` (histogram, ms) plus `tracer.revalidation.{processed,skipped,failed}` counters tagged with `trigger = "auto" | "manual"`. Counter-add is only performed when count > 0 (avoids noisy 0-value events). Always tag with `trigger` — dashboards separate scheduled sweeps from user-initiated drains.
- **`LoggerMessage` source generator partials on `BackgroundService`** — `RevalidationScheduler` is `internal sealed partial class` because it defines 9 `[LoggerMessage]` partial methods. Keep log templates PII-free: only profile GUIDs, counts, durations, and `ex.GetType().Name`. Never interpolate `ex.Message`, `profile.LegalName.Value`, or any enriched field into log messages — CKB payloads include names, addresses, and officers which are PII under GDPR.
- **`BackgroundService` testing seams — `internal Func<DateTimeOffset> Clock` + `internal Func<TimeSpan, CancellationToken, Task> DelayAsync`** — Mirror the `HandelsregisterClient.Clock` pattern. `RevalidationScheduler.RunTickAsync` is `internal` so unit tests can drive a single tick without the outer `ExecuteAsync` loop or real clock delays. `DelayAsync` lets tests of the outer loop run instantly. Same pattern should apply to any future `BackgroundService`.
- **Field TTL policy (B-68)** — `IFieldTtlPolicy` (Application, Singleton) is the authority for "is this field stale?" Merges the Domain baseline (`FieldTtl.For()` in `Tracer.Domain.ValueObjects`) with per-environment overrides from `Revalidation:FieldTtl`. Surface: `GetTtl(FieldName)`, `GetExpiredFields(profile, now)`, `GetNextExpirationDate(profile, now)`, `IsRevalidationDue(profile, now)`. All time-based methods take an explicit `now` — no `IClock` abstraction, callers pass `DateTimeOffset.UtcNow` (or a test value) so successive calls see a consistent snapshot and unit tests stay deterministic. `CompanyProfile.NeedsRevalidation()` stays on Domain defaults for aggregate invariants; Application code MUST go through `IFieldTtlPolicy` so overrides take effect. **RegistrationId is not a `TracedField<T>`** (plain string column, no `EnrichedAt`) and is intentionally excluded from the TTL sweep; **Officers** is GDPR-gated and also excluded.
- **`Revalidation:FieldTtl` binding pattern** — The config section is a flat `FieldName → TimeSpan` map (`"EntityStatus": "30.00:00:00"`), so `.Bind()` can't be used directly (it would need an `Overrides:` sub-object). `Program.cs` uses `.Configure<IConfiguration>((options, configuration) => { ... })` to project children into `FieldTtlOptions.Overrides`. Three hard failures at startup: (1) unparseable TimeSpan → `InvalidOperationException` with key + bad value (silent drop would produce a half-applied policy that's painful to diagnose); (2) `ValidateOnStart` rejects non-positive durations; (3) `ValidateOnStart` rejects keys that aren't `FieldName` members (case-insensitive). `TimeSpan.TryParse` uses `CultureInfo.InvariantCulture` so locale never affects parsing. `FieldTtlPolicy`'s constructor repeats the same guards defensively so unit tests and direct callers can't bypass them.
- **LATAM registry provider family (B-89)** — Four Tier 2 / Priority 200 providers share one `LatamRegistryClient` that dispatches to per-country `ILatamRegistryAdapter` (`ArgentinaAfipAdapter` / `ChileSiiAdapter` / `ColombiaRuesAdapter` / `MexicoSatAdapter`). `ProviderId` convention: `latam-<registry>` (`latam-afip`, `latam-sii`, `latam-rues`, `latam-sat`). `SourceQuality = 0.80` — deliberately below `StateSos` (0.85) because LATAM HTML is less stable and antibot/CAPTCHA walls are common. Shared rate limit is **10 req/min across all four countries** (LATAM registries often share ASN / WAF policy — a per-country limit would still trigger shared blocks). `RegistrationId` is stored as `{CC}:{normalized-identifier}` (e.g. `AR:30500010912`, `CL:96790240-3`, `CO:890903938`, `MX:WMT970714R10`). Adapters MUST return `null` from `Parse` on CAPTCHA/login walls (best-effort skeleton treats them as `NotFound`, not `Error`) — see `MexicoSatAdapter.LooksLikeCaptchaWall`. Status normalization is per-adapter (Spanish `activo/disuelta/suspendida` → canonical English `active/dissolved/suspended`; Colombia adds `in_liquidation`; Mexico uses uppercase SAT terminology).
- **LATAM provider base pattern (B-89)** — `LatamRegistryProviderBase` (abstract, internal) owns `CanHandle` + `EnrichAsync` + stopwatch/error discrimination + field mapping so concrete per-country providers become ~30-line configuration classes (`ProviderId`, `CountryCode`, `GenericErrorMessage`, `IsPossibleCountryIdentifier`, `NormalizeStatus`). Base uses `LoggerMessage.Define` delegates (not the `[LoggerMessage]` source generator) because it's a non-partial abstract class — EventIds 9200/9201/9202 reserved for LATAM logging. **`MapToFields` guards against whitespace-only `CountryCode`/`RegistrationId`** so a mis-parsed page never produces a malformed `"  :123"` CKB key even though the `LatamRegistrySearchResult` record marks both fields `required`.
- **LATAM fallback CanHandle** — Beyond the primary `Country == CC` match, each provider has an identifier-format regex fallback (`IsPossibleCountryIdentifier`) so requests without a country hint still route correctly. The fallbacks are mutually exclusive: Argentina requires exactly 11 digits AND **no letters** (rejects RFC); Mexico RFC regex requires 3–4 leading letters (rejects CUIT/NIT/RUT); Chile RUT regex requires a dash-separated verifier `[\dKk]` (rejects pure-digit NITs); Colombia NIT regex requires 8–10 digits with an optional `-X` verifier (CUIT's 11 digits fall outside). Adding a fifth country means carefully verifying regex non-overlap to avoid two LATAM providers competing for the same identifier.
- **AngleSharp synchronous parsing pattern** — `document = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();` with `#pragma warning disable CA1849`. Used by every scraping adapter (StateSos, Handelsregister, all 4 LATAM). `Content()` parsing is CPU-only (no I/O) so CA1849's "prefer async" warning is a false positive — suppress with a one-line comment linking the reason. Never use `.Result` elsewhere in providers; this is the one permitted exception.
- **Shared TraceContext test factory (B-89)** — `tests/Tracer.Infrastructure.Tests/Providers/LatamRegistry/LatamProviderTestContext.Create(country, registrationId, taxId, companyName, depth, accumulated)` is the canonical way for LATAM provider tests to spin up a `TraceContext`. Saved ~50 LOC of duplication across the four per-country test classes. When adding a fifth LATAM provider test class, reuse this helper rather than inlining another `new TraceContext { Request = new TraceRequest(...) }`.
- **Deep re-validation runner (B-67)** — `DeepRevalidationRunner` is the production `IRevalidationRunner` registered in `ApplicationServiceRegistration` (Scoped). Triggers a full waterfall re-enrichment at `TraceDepth.Standard` whenever `IFieldTtlPolicy.GetExpiredFields(profile, now).Count >= Revalidation:Deep:Threshold` (default 3). Synthesises a `TraceRequest` with `Source = "revalidation"` from the profile (`LegalName`/`TradeName` → `CompanyName`, plus `Phone`/`Email`/`Website`/`TaxId`/`Industry`), delegates merge + change detection + profile persistence to the shared `WaterfallOrchestrator` → `CkbPersistenceService`, then writes a `ValidationRecord` (`ValidationType.Deep`, `ProviderId = "revalidation-waterfall"`) and a terminal `TraceRequest.Complete` under its own `IUnitOfWork.SaveChangesAsync`. Feasibility gate: profiles missing `RegistrationId` or `Country` are skipped (`Deferred` + WARN log) because the waterfall cannot target a registry without them. `NoOpRevalidationRunner` is retained only as a test double.
- **`IRevalidationRunner` save-boundary rule (revised B-67)** — Lightweight runners (B-66) must leave persistence to the scheduler; deep runners reuse `WaterfallOrchestrator`, which already saves the profile internally via `CkbPersistenceService`. `DeepRevalidationRunner` therefore owns two additional `SaveChangesAsync` checkpoints: one after staging the `InProgress` `TraceRequest` (to get a persisted ID), and a final one after `ValidationRecord` + `MarkValidated` + `TraceRequest.Complete`. On orchestrator cancellation or failure the runner best-effort marks the trace `Failed` via `CancellationToken.None` so cancellation does not leak `InProgress` trace rows, logging only `ex.GetType().Name` (CWE-209). The `IRevalidationRunner` docstring captures this per-mode distinction.
- **Revalidation FieldsChanged metric** — `ValidationRecord.FieldsChanged` is computed as `Math.Max(0, IChangeEventRepository.CountByProfileAsync(after) - CountByProfileAsync(before))`. The `Math.Max` clamp is defensive — the orchestrator does not prune change events, but a negative delta would throw inside `ValidationRecord`'s non-negative guard. `FieldsChecked = expiredFields.Count` (the fields the scheduler wanted re-verified), not the total set visited by the waterfall, so audit entries mirror the scheduler's intent rather than the orchestrator's breadth.
- **Frontend UI primitives (B-88)** — Loading, empty and error UX are now centralised in `src/Tracer.Web/src/components/`: `skeleton/Skeleton.tsx` (base + `SkeletonLine` / `SkeletonCard` / `SkeletonTable`), `EmptyState.tsx` (icon + title + description + action, renders `<Link>` or `<button>`), `ErrorMessage.tsx` (inline `role="alert"` block with optional Retry), `ErrorBoundary.tsx` (top-level class component wrapping `<BrowserRouter>` in `main.tsx`; stack only in `import.meta.env.DEV`). Every new page MUST use these primitives instead of the ad-hoc `text-center py-10 text-gray-500 "Loading..."` / `bg-red-50 …` snippets — consistency + a11y are already baked in.
- **Frontend toast system (B-88)** — `components/toast/ToastProvider.tsx` + `useToast()` is a zero-dependency, 5-toast bounded, auto-dismissing (5 s default) notification surface wired once in `main.tsx` around the router. Two ARIA live regions split by urgency: `success`/`info` ride `role="status"` (polite), `warning`/`error` ride `role="alert"` (assertive). Do NOT add `react-hot-toast` / `sonner` etc. — every new runtime dep is a CVE vector and the current system is sufficient. To surface SignalR events globally, subscribe via `useGlobalToasts({ onTraceCompleted, onChangeDetected })` inside `Layout`; call this hook exactly once. `Minor`/`Cosmetic` `ChangeDetectedEvent`s are intentionally silent — they would swamp users during re-validation bursts; users see them on the Change Feed page only.
- **Responsive + a11y Layout (B-88)** — `Layout.tsx` sidebar is static from Tailwind's `md` breakpoint up and a slide-in overlay below it, toggled via a hamburger button in a mobile top bar. Four non-obvious invariants when modifying this file: (1) the off-screen mobile sidebar MUST keep `inert={!isDesktop && !mobileSidebarOpen}` so Tab order / AT tree skip it — `-translate-x-full` alone still leaves it focusable. (2) The `aside` carries `id="primary-navigation"` so the hamburger's `aria-controls` resolves. (3) Do NOT introduce `useEffect` that calls `setState` to reset the sidebar on route/viewport change — `react-hooks/set-state-in-effect` will reject it; derive state instead (`sidebarOpen = !isDesktop && mobileSidebarOpen`) and close on NavLink `onClick`. (4) The skip-link is the first focusable element in the tree and targets `#main-content` (`<main tabIndex={-1}>`); don't remove either side of this pair.
- **`useMediaQuery` initial-sync pitfall (B-88)** — `hooks/useMediaQuery.ts` intentionally does NOT re-sync `matches` inside its subscribe effect — the `useState(() => mql.matches)` initialiser already captures the current value, and a later `setMatches(mql.matches)` would violate `react-hooks/set-state-in-effect`. Accept the micro-race between render and `addEventListener` — real queries fire `change` before layout matters.
- **Shadow-property aggregates on `CompanyProfile` (B-91)** — `OverallConfidence` is a `Confidence?` value object exposed through a shadow property via an explicit `ValueConverter<Confidence?, double?>`. The canonical aggregate pattern is `query.Select(p => EF.Property<double?>(p, "OverallConfidence")).AverageAsync(ct)`, which EF Core translates to `AVG([OverallConfidence])` — SQL Server's `AVG` ignores NULLs. The return type is `double?`; always null-coalesce to `0.0` at the repository boundary so callers never see `null` and the dashboard can treat `0.0` as the "no data" signal (the React side already renders a dash for it). Same pattern should be used for any future aggregate over a VO-backed column.
- **ChangeEvent JSON payloads are not the PII boundary (B-91)** — `ChangeEvent.PreviousValueJson` / `NewValueJson` are the last-mile persistence, but the GDPR boundary is **upstream** in `WaterfallOrchestrator`, which strips personal-data fields via `IGdprPolicy.PersonalDataFields` before `CompanyProfile.UpdateField()` ever runs. When adding a new personal-data field, register it in `GdprPolicy.Classify()` — do NOT add field-level redaction in `ChangeEvent` or the JSON serializer. Double-handling creates two sources of truth and drift-risk.
- **Test coverage baseline & gap doc (B-91)** — `docs/testing/coverage-baseline.md` tracks the canonical release-candidate test snapshot: per-project source LOC, test file / test-method counts, plus an explicit gap table (Query handlers without fixtures, repositories without a DbContext integration harness, Redis / frontend gaps). Update it whenever the numbers drift materially or a gap is closed; the reproduction commands at the top of the file are the single source of truth for how to measure.
- **Change-event topic routing (B-74)** — Two subscriptions on `tracer-changes`: `fieldforce-changes` (SQL filter `Severity='Critical' OR Severity='Major'`) and `monitoring-changes` (implicit `1=1` TrueFilter; no `$Default` override). `CriticalChangeNotificationHandler` publishes Critical; `FieldChangedNotificationHandler` publishes Major + Minor and is explicitly early-returned for Critical to avoid double-publish (both events fire for the same change — `CompanyProfile.UpdateField` raises `FieldChangedEvent` always + `CriticalChangeDetectedEvent` for Critical). Cosmetic is never published (log-only — would swamp monitoring with no business value). SignalR push is reserved for Critical + Major; Minor is polled from `/api/changes`. Severity string used for routing is `enum.ToString()` — Contracts and Domain enum member names must stay aligned (enforced by `MapEnum<,>`).
- **Service Bus DLQ flags** — Every subscription and queue that can receive messages must set `deadLetteringOnMessageExpiration: true` AND `deadLetteringOnFilterEvaluationExceptions: true` on subscriptions. The filter-exception flag is the non-obvious one: without it a malformed SQL filter silently drops messages with no telemetry. See `deploy/bicep/modules/service-bus.bicep` — both `fieldforce-changes` and `monitoring-changes` have both flags set. `monitoring-changes` uses `maxDeliveryCount: 10` (vs 5 for fieldforce) to tolerate transient monitoring-tool outages since those consumers are internal and lower-criticality.
- **`ChangeEventSubscriptionRoutingTests` pattern (B-74)** — Integration-style unit test that mirrors the Bicep SQL filter as a `Predicate<string>` in code and drives real handlers through a capturing `IServiceBusPublisher`. Acts as a drift detector: if either the Bicep filter or `ServiceBusPublisher.PublishChangeEventAsync` (which sets `ApplicationProperties["Severity"] = severity.ToString()`) changes, the test fails. Same pattern should apply to any future topic where routing semantics live in both infra and code — keep the test at the Application layer, not Infrastructure, so it runs on every CI build without a live Service Bus.
- **Distributed cache provider toggle (B-79)** — `Cache:Provider` (`InMemory` | `Redis`) drives the `IDistributedCache` registration in `Tracer.Infrastructure.Caching.DistributedCacheRegistration`. Default = `InMemory` so dev / CI need no configuration; production opts in to Redis by setting `Cache:Provider = Redis` and `ConnectionStrings:Redis`. `CacheOptions` is bound + `ValidateOnStart`'d so a Redis misconfig fails at boot, not at first cache hit. **Single source of truth for `Cache:Provider`** is `CacheOptions.ResolveProvider(IConfiguration)` — `DistributedCacheRegistration` and `AddInfrastructureHealthChecks` both call it; never re-implement the parse with `Enum.TryParse` directly. `RedisInstanceName` (default `"tracer:"`) prefixes all keys so multiple environments can share a Redis instance without collisions.
- **`AddInfrastructure(connectionString, configuration)` signature** — B-79 added the `IConfiguration` parameter so the cache branch is selected from config at registration time. The single production caller is `Program.cs`. Tests that build the Infrastructure DI in isolation must pass a non-null `IConfiguration` (an empty `ConfigurationBuilder().Build()` is fine and selects the in-memory cache).
- **`RedisHealthCheck` returns `Degraded`, never `Unhealthy`** — the cache is an optimisation; `Unhealthy` would trigger Azure App Service auto-restarts on transient Redis hiccups. The probe writes + reads + removes a `health:probe:{guid}` key with a 5-second TTL. Description strings include only `ex.GetType().Name` (CWE-209 — StackExchange.Redis exception messages can echo connection-string credentials in inner exceptions). Registration is conditional in `AddInfrastructureHealthChecks(IConfiguration)` — only added when `Cache:Provider = Redis`.
- **`CacheWarmingService` is opt-in (`Cache:Warming:Enabled`)** — Singleton `BackgroundService` registered in `Program.cs` only when the flag is true. Loads the top-N profiles via `ICompanyProfileRepository.ListTopByTraceCountAsync` (filtered descending index on `TraceCount` added in `CompanyProfileConfiguration`), then writes them via `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 16` — sequential `SetAsync` would be O(N × RTT), minutes long for 10 000 profiles at 5 ms RTT. Per-profile failures use `Interlocked.Increment` for `loaded`/`failed` counters and never propagate. `MaxProfiles` is range-validated `[1, 10_000]`. PII-free logging — only profile GUID, counts, and `ex.GetType().Name`.
- **Bicep secret outputs use `@secure()`** — `redis.bicep` decorates the connection-string output with `@secure()` so the value is redacted in Azure deployment history. `main.bicep` consumes it directly in a `Microsoft.KeyVault/vaults/secrets` child resource (`ConnectionStrings--Redis`); never pass a secret through a non-`@secure()` output. `redisCacheName` is exposed as a normal output (non-sensitive). Apply the same pattern for any future module that emits credentials.
- **Cache configuration keys (B-79)** — `Cache:Provider`, `Cache:ProfileTtl`, `Cache:RedisInstanceName`, `Cache:Warming:Enabled`, `Cache:Warming:MaxProfiles`, `Cache:Warming:DelayOnStartup`. `ConnectionStrings:Redis` is required only when Provider = Redis. The Bicep app-service module sets `Cache__Provider = Redis` for production deployments by default.
- **EF Core: filtered descending index for hot-list queries** — `CompanyProfileConfiguration` declares `HasIndex(e => e.TraceCount).IsDescending().HasFilter("[IsArchived] = 0")`. Used by `ListTopByTraceCountAsync` (cache warming), `ListByCountryAsync` (fuzzy candidates), and `ListAsync`. The same filter pattern is used for `LastValidatedAt`. SQL Server-only — integration tests must use the SQL Server Testcontainer, not SQLite. **Generate a migration before deploying** — adding `HasIndex` to the model does not retroactively create the SQL index; run `dotnet ef migrations add AddTraceCountIndex` from `src/Tracer.Infrastructure`.
- **E2E test harness (B-77)** — Phase 3 Deep-flow E2E lives in `tests/Tracer.Infrastructure.Tests/Integration/`. Layout: `DeepFlowE2ETests.cs` (4 tests) + `Integration/Fakes/` (6 in-memory repos, `FakeEnrichmentProvider`, `FakeLlmDisambiguatorClient`, `StubFuzzyNameMatcher`, `InMemoryUnitOfWork`). The test host wraps `WebApplicationFactory<Program>` and swaps out every external dependency at the DI boundary via `services.RemoveAll<T>()` + re-registration in `ConfigureTestServices`: all `IEnrichmentProvider`s → `FakeEnrichmentProvider` instances keyed by priority tier; all five repositories + `IUnitOfWork` → in-memory equivalents; `ILlmDisambiguatorClient` → fake; `IFuzzyNameMatcher` → stub (optional). **Do not mock `IWaterfallOrchestrator`, `ICkbPersistenceService`, `IEntityResolver`, `ILlmDisambiguator` or `IChangeDetector`** — the whole point of the E2E suite is to exercise those real services through the HTTP entry point. New E2E scenarios should follow the same pattern: seed in-memory state, configure fakes, POST to `/api/trace`, assert against `host.Profiles.All` / `host.Changes.All`.
- **`InMemoryUnitOfWork` must dispatch domain events** — `CkbPersistenceService` calls `IUnitOfWork.SaveChangesAsync` twice per persist: first call must dispatch `FieldChangedEvent` / `CriticalChangeDetectedEvent` via `IMediator.Publish` so handlers run before `MarkNotified`, second call flushes the `IsNotified` flag. A no-op unit of work silently drops notifications and breaks change-detection assertions. The in-memory implementation mirrors `TracerDbContext.SaveChangesAsync` — snapshot events, clear on entities, dispatch. **Seed helpers must call `profile.ClearDomainEvents()` before storing**, otherwise construction-time `ProfileCreatedEvent` + per-field `FieldChangedEvent` would fire on the first real save and corrupt `Changes.All` assertions.
- **Fake provider DI — Singleton, not Transient** — Production providers are Transient so the typed-HttpClient factory manages handler lifetime. Test fakes have no HTTP dependency and must be Singleton so the test can read `fake.Invocations` after the HTTP round-trip on the same instance the orchestrator invoked. Registration pattern: `services.AddSingleton<IEnrichmentProvider>(_ => fake)` — one registration per fake; DI collects them via `IEnumerable<IEnrichmentProvider>`.
- **`WebApplicationFactory<Program>` + `TreatWarningsAsErrors` — CA2000 pragma required** — Creating the factory inside a helper method and returning it for `await using` by the caller trips CA2000 ("dispose before losing scope"). Wrap the `new WebApplicationFactory<Program>()...` expression in `#pragma warning disable CA2000 ... #pragma warning restore CA2000`. Consumers then use block syntax `await using (factory.ConfigureAwait(true)) { ... }` so CA2007 (ConfigureAwait on implicit DisposeAsync) stays satisfied. Both pragmas are established in `BatchEndpointPublishTests` (B-44) and the Phase-3 E2E suite (B-77).
- **Deterministic fuzzy scoring in E2E** — Real `FuzzyNameMatcher` combined with the 8-step `CompanyNameNormalizer` makes it painful to land test names precisely in the 0.70–0.85 mid-tier band. `StubFuzzyNameMatcher` in `tests/Tracer.Infrastructure.Tests/Integration/Fakes/` takes a `candidateName → score` map and returns exact values, so LLM-escalation tests don't have to encode Jaro-Winkler+Jaccard math. Register via `services.AddSingleton<IFuzzyNameMatcher>(stub)` — note the explicit generic; `services.AddSingleton(stub)` would register the concrete type and the orchestrator would keep resolving the real matcher.
- **OpenAPI documentation (B-82)** — `AddOpenApi()` (from `Microsoft.AspNetCore.OpenApi`) is enriched with two transformers in `src/Tracer.Api/OpenApi/`: `TracerOpenApiDocumentTransformer` fills `Info`, `Servers`, `Tags` and the `ApiKey` (`X-Api-Key` header) security scheme; `ApiKeySecurityRequirementTransformer` attaches the requirement to every operation except the allowlist `/health` + `/openapi/*` that mirrors `ApiKeyAuthMiddleware`. Both transformers are Singletons — options are captured once via `IOptions<T>` to match `ValidateOnStart` semantics. `TracerOpenApiOptions` (`"OpenApi"` section) is bound with `ValidateDataAnnotations` + a custom `ServerUrls` absolute-URI check + `ValidateOnStart`, so misconfig fails at boot. The spec endpoint (`/openapi/v1.json`) is **always** mapped because the OpenAPI JSON is the integrator contract; the interactive Scalar UI (`/scalar/{documentName}`) is mounted only in Development or when `OpenApi:EnableUi=true`. Tests: `tests/Tracer.Infrastructure.Tests/OpenApi/OpenApiDocumentTests.cs` spins up `WebApplicationFactory<Program>`, loads the spec JSON and asserts info / security scheme / per-operation security requirements / tag descriptions / UI gating.
- **Microsoft.OpenApi 1.x vs 2.x collection types** — `OpenApiDocument.Tags` is `IList<OpenApiTag>` in Microsoft.OpenApi 1.x but `ISet<OpenApiTag>` in 2.x (preview). `TracerOpenApiDocumentTransformer` populates `document.Tags` in place via `.Add(...)` instead of replacing the collection, so the same code compiles and works across the two preview surfaces ASP.NET Core preview ships against. `Components.SecuritySchemes` is similarly left to its auto-initialized state (do **not** reassign it). Rule of thumb for future transformers: never replace OpenApiDocument collections — mutate them in place.
- **XML doc generation is Api-project-scoped** — `<GenerateDocumentationFile>true</GenerateDocumentationFile>` lives in `src/Tracer.Api/Tracer.Api.csproj`, NOT `Directory.Build.props`. Turning it on globally would demand XML docs on every internal helper in Domain / Application / Infrastructure (breaks build under `TreatWarningsAsErrors`). CS1591 is suppressed only inside Tracer.Api so `Program.cs` partial and unannotated private helpers do not fail the build; public endpoints, DTOs, commands and queries carry real `<summary>`.
- **Scalar.AspNetCore UI gating** — Production hosts must set `OpenApi:EnableUi=true` to expose the interactive UI; otherwise only the raw spec is served. `appsettings.Development.json` sets `EnableUi=true` so `dotnet run` gives an out-of-the-box interactive explorer at `/scalar/v1`. Scalar loads its front-end assets from jsdelivr CDN; hosts with strict CSP requirements should self-host Scalar assets or gate the UI off entirely (follow-up for B-87 security hardening).
- **Performance testing harness (B-86)** — Two complementary suites, both opt-in and never on the default CI path. (1) **BenchmarkDotNet** lives in `tests/Tracer.Benchmarks/` — a **console exe**, not a test project, so `dotnet test` never picks it up. Each benchmark class is `public` (BDN reflects on it), `[MemoryDiagnoser]`, and references only the **public** Application API — no `InternalsVisibleTo` back-channel, because the point is to measure the production surface. Add new benchmarks by dropping a class under `tests/Tracer.Benchmarks/Benchmarks/`; `BenchmarkSwitcher.FromAssembly(...)` auto-discovers them. (2) **k6 scripts** in `deploy/k6/` encode SLO thresholds inline (`options.thresholds`) so k6 exits non-zero on regression, making them usable as deploy gates. `deploy/scripts/run-benchmarks.sh` and `run-load-test.sh` fail fast on missing `BASE_URL` / `API_KEY`. `.github/workflows/perf.yml` is `workflow_dispatch`-only with `job = benchmarks | load-test | both`; `PERF_API_KEY` is a repo secret that the job verifies explicitly.
- **Benchmark payload hygiene (B-86)** — k6 scripts **must not** submit real company names, phone numbers, addresses, or emails. Use deterministic fictitious values (`Contoso International Testing Ltd.`, `Load Test Company NNN`, `+420 000 000 000`, `example.invalid`) and a neutral country code. Reason: load tests hit the real waterfall, which writes to CKB and publishes to Service Bus — any real data would create fake golden records that would need GDPR erasure. Same rule for BenchmarkDotNet fixtures in `Fixtures/SampleData.cs`.
- **Batch endpoint rate-limit in load tests (B-86)** — `batch-load.js` sleeps 12 s between iterations so 1 VU over 60 s produces exactly 5 requests — the ceiling of the `batch` rate-limit policy (5 req/min, `QueueLimit = 0`). Any future k6 script that targets `/api/trace/batch` must respect the same pacing; the limiter returns 429 immediately otherwise and thresholds read as errors even though the SUT is healthy.
- **Security response headers (B-87)** — `SecurityHeadersMiddleware` (Api layer) emits `Content-Security-Policy` (default `default-src 'none'; frame-ancestors 'none'; base-uri 'none'` — Tracer API never returns HTML), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Resource-Policy: same-origin`. Headers are applied via `context.Response.OnStarting` so they survive exception handler / 4xx paths. The `Server` header is stripped defensively. `SecurityHeadersOptions.Enabled = false` disables this middleware (the built-in `UseHsts()` still emits `Strict-Transport-Security`). Pipeline order matters: `UseForwardedHeaders → UseExceptionHandler → UseStatusCodePages → UseHsts (prod only) → UseHttpsRedirection → UseSecurityHeaders → UseCors → UseSerilogRequestLogging → UseApiKeyAuth → UseRateLimiter`.
- **HSTS production-only** — `app.UseHsts()` is gated on `!app.Environment.IsDevelopment()`. In dev we skip it so localhost HTTPS dev certs do not become permanently pinned in browsers. Integration tests that want to assert HSTS must call `WithWebHostBuilder(b => b.UseEnvironment("Production"))` and also seed `Cors:AllowedOrigins` (production enforces it). `HstsOptions.MaxAge / IncludeSubDomains / Preload` are bound from the same `Security:Headers` section that drives `SecurityHeadersOptions` — default is 2 years + `includeSubDomains`, preload opt-in only.
- **API key rotation (B-87)** — `Auth:ApiKeys` accepts two shapes: flat string form (`Auth:ApiKeys:0 = "key"` — backwards compatible) and structured form (`Auth:ApiKeys:0:Key = "key"` + optional `:Label` + `:ExpiresAt` ISO 8601). `ApiKeyOptionsBinder` handles both in a single array. `ApiKeyOptionsValidator` + `ValidateOnStart()` rejects keys shorter than 16 characters, duplicates, and already-expired `ExpiresAt` at boot — fail-fast matches the GDPR / FieldTtl patterns. `ApiKeyAuthMiddleware` re-checks `IsActive(now)` on every request via injected `TimeProvider`, so an operator can roll a new key, wait, then let the old one expire without a redeploy.
- **Caller identity in `HttpContext.Items`** — On successful auth, `ApiKeyAuthMiddleware` writes `Items["ApiKeyFingerprint"] = "apikey:<sha256 prefix>"` (server-derived, 8 hex chars) and `Items["AuthLabel"] = <configured label>`. `ApiKeyAuthMiddleware.CallerFingerprintItemKey` + `CallerLabelItemKey` are the canonical constants. Audit pipelines (B-70 GDPR export, B-85 manual override audit) must read these and **never** accept caller identity from request bodies — the body is untrusted.
- **`TimeProvider` over `IClock`** — Injected as `TimeProvider.System` in `Program.cs` so middleware / services can be unit-tested with a custom clock (e.g. the `FrozenTimeProvider` harness in `ApiKeyAuthTests`). Prefer this over a bespoke `IClock` abstraction; .NET 10 `TimeProvider` is the canonical testable time source.
- **CI vulnerability scanning (B-87)** — `.github/workflows/ci.yml` runs `dotnet list package --vulnerable --include-transitive` after `dotnet test` and greps the output for `High` / `Critical` rows; any match fails the pipeline. Frontend job runs `npm audit --audit-level=high`. Moderate / Low findings stay informational until the tree is clean — tighten by lowering the threshold. `dotnet list package --vulnerable` returns exit 0 even with findings, which is why the grep gate is necessary.
- **Validation dashboard endpoints (B-71)** — `GET /api/validation/stats` returns `ValidationStatsDto` (pending count, processed-today, changes-today, average data age in days). `GET /api/validation/queue?page&pageSize` returns `PagedResult<ValidationQueueItemDto>`. Queue handler overfetches `(page+1) × pageSize` profiles (hard-capped by `ListValidationQueueHandler.MaxQueueSweep = 500`), filters via `IFieldTtlPolicy.GetExpiredFields`, then paginates the filtered slice in-memory. `TotalCount` comes from `ICompanyProfileRepository.CountRevalidationCandidatesAsync` — an upper bound over non-archived profiles, not the exact expired-field subset; the UI documents this as an approximation. Manual enqueue continues to reuse the existing `POST /api/profiles/{id}/revalidate` endpoint — the dashboard calls it directly and invalidates the two TanStack Query keys (`['validation-stats']`, `['validation-queue']`) on success.
- **`CountRevalidationCandidatesAsync` / `AverageDaysSinceLastValidationAsync`** — `AverageDaysSinceLastValidationAsync` intentionally materialises `{LastValidatedAt, CreatedAt}` rows client-side because EF Core can't translate `DateTimeOffset` subtraction into SQL uniformly across providers; the query is bounded by the non-archived profile count and is a dashboard-only read. Profiles that have never been validated fall back to `CreatedAt`. A production-scale replacement would precompute this into a column or materialised view — tracked as a future optimisation.
- **Frontend SignalR — single owner per event, Layout subscribes for dashboards** — Live invalidation hooks (`useChangeFeedLiveUpdates`, `useValidationLiveUpdates`) accept the `on*` subscription factory from an outer `useSignalR()` consumer. The owner is `components/Layout.tsx` because it holds the one `useSignalR()` call for the entire session. Calling `useSignalR()` inside a hook that is already consumed by Layout would register a second set of lifecycle callbacks on the module-level singleton connection and silently break reconnection state tracking. **Rule:** any new "invalidate on SignalR event" hook follows this factory-injection pattern, and the wiring goes in `Layout.tsx`.
- **Batch export (B-81)** — `ICompanyProfileExporter` and `IChangeEventExporter` (Application, Scoped) stream CKB profiles / change events to CSV or XLSX on `GET /api/profiles/export` and `GET /api/changes/export`. Packages: `CsvHelper` (streaming CSV via `AsAsyncEnumerable`) and `ClosedXML` (in-memory XLSX). Row cap: `ExportLimits.MaxRows = 10_000` (clamped twice — endpoint validates `maxRows ∈ [1, 10 000]`, exporter re-clamps; defense-in-depth). Repositories expose `StreamAsync(int maxRows, ...)` returning `IAsyncEnumerable<T>` with SQL-side `Take(maxRows)` and `AsNoTracking` — the enumerable MUST be consumed within the repository's DbContext scope. CSV streaming disables ASP.NET response buffering via `IHttpResponseBodyFeature.DisableBuffering()` so rows flush to the client live; XLSX is built in memory (≤ 12 MB at cap) and written in one go.
- **CSV formula injection (CWE-1236) — `CsvInjectionSanitizer`** — Central `static` helper in `Tracer.Application.Services.Export/`. Prefixes an apostrophe to any cell whose first char is `=`, `+`, `-`, `@`, TAB, or CR so Excel / LibreOffice treat it as literal text. Applied at the mapping layer (`ExportMappingExtensions.ToExportRow`) so every cell — including `PreviousValueJson`/`NewValueJson` from `ChangeEvent` — is sanitised. Covers both CSV and XLSX output (XLSX cells interpret formulas identically). Any new export column MUST route through the sanitiser; don't bypass with a raw string write.
- **`export` rate limit policy** — `Program.cs` registers a `FixedWindow` partition per client IP: `PermitLimit = 10`, `Window = 1 min`, `QueueLimit = 0`. Applied via `.RequireRateLimiting("export")` on the export endpoints. Separate from the `batch` policy (`PermitLimit = 5`) because exports are IO-heavy (up to 10 000 rows) and deserve their own budget. Rejection → 429 + ProblemDetails (default rejection status). Any future expensive read endpoint should either reuse `export` or introduce its own named policy — do not share with `batch`.
- **Export endpoint pattern (IResult + response body)** — Export handlers do NOT go through MediatR; they write directly to `HttpContext.Response.Body` so CSV streaming works. Return `TypedResults.Empty` (no-op `IResult`) after writing. Validation runs before touching the response (Content-Type / Content-Disposition are only set once input is known valid). Any future streaming-response endpoint should follow the same order: `(1)` validate query, return `TypedResults.Problem` on failure, `(2)` set Content-Type + Content-Disposition, `(3)` write body, `(4)` return `TypedResults.Empty`.
- **Frontend blob download (`downloadExport` helper)** — `src/Tracer.Web/src/api/client.ts` exports `downloadExport(path, fallbackFileName)` that does fetch → `response.blob()` → transient `<a>` + `URL.createObjectURL` + `revokeObjectURL` in a `finally`. Honours server `Content-Disposition` filename when present. `profileApi.export` and `changesApi.export` are thin wrappers that build the query string and pick the fallback filename. Export buttons on `ProfilesPage` and `ChangeFeedPage` disable *both* Export buttons while any export is in progress (single `exportingFormat` state) so the user can't trigger concurrent downloads.

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

# Smoke test Phase 2 (po deployi na Azure)
./deploy/scripts/smoke-test-phase2.sh https://tracer-test-api.azurewebsites.net <API_KEY>
# Deployment runbook: deploy/DEPLOYMENT.md

# Performance testing (B-86) — opt-in, neblokuje PR merge
./deploy/scripts/run-benchmarks.sh                                                    # BenchmarkDotNet micro-benchmarks
./deploy/scripts/run-benchmarks.sh "*FuzzyMatcher*"                                   # filtered subset
./deploy/scripts/run-load-test.sh trace-smoke https://tracer-test-api... <API_KEY>    # k6 load test
# Manual-dispatch: Actions → Performance (docs: docs/performance/README.md)

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
- `Providers:AzureMaps:SubscriptionKey` — Azure Maps subscription key
- `Providers:AbnLookup:Guid` — ABN Lookup registration GUID (optional, AU companies)
- `Providers:AzureOpenAI:Endpoint` — Azure OpenAI endpoint
- `Providers:AzureOpenAI:ApiKey` — Azure OpenAI API key (optional; falls back to DefaultAzureCredential/Managed Identity when absent)
- `Revalidation:Enabled` — toggle re-validation engine (default: true)
- `Revalidation:IntervalMinutes` — scheduler interval (default: 60)
- `Revalidation:MaxProfilesPerRun` — batch size (default: 100)
- `Cache:Provider` — `InMemory` (default) or `Redis` (B-79)
- `Cache:Warming:Enabled` — toggle startup cache pre-population (default: false)
- `Cache:Warming:MaxProfiles` — top-N profiles to warm (1–10000, default: 1000)
- `ConnectionStrings:Redis` — required when `Cache:Provider = Redis`; sourced from Key Vault secret `ConnectionStrings--Redis`
- `OpenApi:Title` — OpenAPI document title (default: `"Tracer API"`)
- `OpenApi:Version` — document version (default: `"v1"`)
- `OpenApi:Description` / `OpenApi:ContactName` / `OpenApi:ContactEmail` / `OpenApi:LicenseName` / `OpenApi:LicenseUrl` / `OpenApi:TermsOfService` — populate the `info` block
- `OpenApi:ServerUrls` — absolute URIs advertised in the `servers[]` array
- `OpenApi:EnableUi` — mount Scalar UI at `/scalar/{documentName}` (default: `false`; Development overrides to `true`)

**Key Vault secret naming:** Azure Key Vault neumožňuje `:` v názvech secrets. Používej `--` jako separator — App Service automaticky překládá `--` → `:`. Příklad: secret `ConnectionStrings--TracerDb` → config key `ConnectionStrings:TracerDb`. Platí pro všechny `@Microsoft.KeyVault()` reference v `app-service.bicep`.

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
