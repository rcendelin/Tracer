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
