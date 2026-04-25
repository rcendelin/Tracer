# Tracer — Company Data Enrichment Engine

Standalone .NET 10 microservice that enriches partial company information (name, registration ID, address) into comprehensive company profiles using free public data sources. Builds a persistent **Company Knowledge Base (CKB)** that grows with every query and monitors field-level changes.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- SQL Server (LocalDB, Docker, or Azure SQL Serverless)

### Backend

```bash
# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Configure secrets (first time only)
cd src/Tracer.Api
dotnet user-secrets set "ConnectionStrings:TracerDb" \
  "Server=(localdb)\mssqllocaldb;Database=TracerDev;Trusted_Connection=true"
dotnet user-secrets set "Providers:GoogleMaps:ApiKey" "<YOUR_KEY>"
dotnet user-secrets set "Providers:AzureMaps:SubscriptionKey" "<YOUR_KEY>"
dotnet user-secrets set "Providers:CompaniesHouse:ApiKey" "<YOUR_KEY>"  # optional, UK
dotnet user-secrets set "Providers:AbnLookup:Guid" "<YOUR_GUID>"        # optional, AU

# Apply EF Core migrations
dotnet ef database update

# Start API (https://localhost:7100)
dotnet run
```

### Frontend

```bash
cd src/Tracer.Web
npm install
npm run dev   # http://localhost:5173
```

### Smoke Test (Phase 2)

```bash
./deploy/scripts/smoke-test-phase2.sh https://tracer-test-api.azurewebsites.net <API_KEY>
```

## Architecture

Clean Architecture — dependencies flow inward only.

```
Tracer.Api           → Minimal API endpoints, SignalR hub, middleware, Program.cs
Tracer.Application   → CQRS (MediatR), orchestrator, services, DTOs
Tracer.Domain        → Entities, value objects, interfaces, domain events
Tracer.Infrastructure→ EF Core, HTTP providers, Service Bus, caching
Tracer.Contracts     → Shared NuGet — Service Bus message contracts for FieldForce
Tracer.Web           → React 19 SPA (Vite 6, TanStack Query v5, Tailwind CSS)
```

### Key Patterns

- **Waterfall Orchestrator** — Tier 1 providers (priority ≤ 100) run in parallel via `Task.WhenAll` with per-provider 15s timeout. Tier 2+ run sequentially with accumulated fields from Tier 1.
- **TracedField\<T\>** — Every enriched field carries confidence score (0.0–1.0), source ID, and timestamp.
- **Golden Record Merger** — Conflict resolution when multiple providers return different values for the same field.
- **CKB** — Persistent company profile database. Second trace on the same company returns cached data instantly (7-day TTL, configurable).
- **Change Detection** — Field-level change detection with severity classification (Critical/Major/Minor/Cosmetic). Critical changes trigger Service Bus + SignalR notifications.

## Enrichment Providers (Phase 2)

| Provider | Region | Priority | Key Data |
|----------|--------|----------|----------|
| ARES | CZ/SK | 10 | IČO, legal form, address, VAT |
| Companies House | UK | 10 | CRN, SIC codes, officers, PSC |
| ABN Lookup | AU | 10 | ABN, entity type, GST status |
| SEC EDGAR | US | 10 | CIK, XBRL financials, filings |
| GLEIF LEI | Global | 30 | Legal name, address, parent chain |
| Google Maps Places | Global | 50 | Address, phone, website, GPS |
| Azure Maps | Global | 50 | Standardized geocoded address |

All providers are free — no paid commercial APIs.

## API Reference

### Trace (Enrichment)

```
POST /api/trace                  Submit enrichment request → 201 Created (sync)
GET  /api/trace/{traceId}        Get trace status and result
POST /api/trace/batch            Submit batch (≤200 items) → 202 Accepted (async via SB)
```

**Request body:**
```json
{
  "companyName": "ŠKODA AUTO a.s.",
  "registrationId": "00177041",
  "country": "CZ",
  "depth": "Standard"
}
```

**Depth levels:** `Quick` (<5s, cache + fastest APIs), `Standard` (<10s, full waterfall), `Deep` (<30s, + scraping + AI)

**Auth:** `X-Api-Key: <key>` header required in production.

### Company Knowledge Base (CKB)

```
GET  /api/profiles               List profiles (paged, filterable by country, confidence)
GET  /api/profiles/{id}          Get profile with enriched fields
GET  /api/profiles/{id}/history  Change history timeline
POST /api/profiles/{id}/revalidate  Trigger re-validation
PUT  /api/profiles/{id}/fields/{field}  Manual field override
```

### Change Feed

```
GET /api/changes              List change events (filterable by severity)
GET /api/changes/stats        Aggregated statistics
```

### Validation

```
GET /api/validation/stats     Re-validation engine status
GET /api/validation/queue     Profiles pending re-validation
```

### SignalR Hub: `/hubs/trace`

Real-time events:

| Event | Scope | Payload |
|-------|-------|---------|
| `SourceCompleted` | Group (traceId) | providerId, status, fieldsEnriched, durationMs |
| `TraceCompleted` | Group (traceId) | traceId, overallConfidence |
| `ChangeDetected` | All clients | changeEventDto |
| `ValidationProgress` | All clients | profileId, progress |

Client must call `SubscribeToTrace(traceId)` to join the trace group.

### Service Bus (Async)

| Resource | Type | Purpose |
|----------|------|---------|
| `tracer-request` | Queue | Inbound requests from FieldForce |
| `tracer-response` | Queue | Outbound results to FieldForce |
| `tracer-changes` | Topic | Change notifications (Critical + Major) |

## Configuration

| Key | Description | Required |
|-----|-------------|----------|
| `ConnectionStrings:TracerDb` | Azure SQL / LocalDB connection string | Yes |
| `ConnectionStrings:ServiceBus` | Azure Service Bus connection string | Async only |
| `Providers:GoogleMaps:ApiKey` | Google Maps Places API (New) | Yes |
| `Providers:AzureMaps:SubscriptionKey` | Azure Maps subscription key | Yes |
| `Providers:CompaniesHouse:ApiKey` | Companies House REST API key | UK enrichment |
| `Providers:AbnLookup:Guid` | ABN Lookup GUID | AU enrichment |
| `Auth:ApiKeys` | Array of valid API keys (empty = open in dev) | Production |
| `Cache:ProfileTtl` | CKB cache TTL (default: `7.00:00:00`) | No |
| `ServiceBus:RequestQueueName` | SB queue for inbound requests | Async only |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights telemetry | Production |

All production secrets are stored in Azure Key Vault. Key Vault secret names use `--` instead of `:` (e.g., `ConnectionStrings--TracerDb`).

## Deployment

See [`deploy/DEPLOYMENT.md`](deploy/DEPLOYMENT.md) for the full manual runbook.

### GitHub Actions (Automated)

Push to `main` triggers:
1. Build API + Web artifacts
2. Deploy Bicep infrastructure (App Service, SQL, Key Vault, Service Bus, App Insights)
3. Populate Key Vault secrets
4. Deploy API to App Service, Web to Static Web App
5. Run smoke tests

Manual deployment: **Actions → Deploy to Azure → Run workflow → select environment**.

### Infrastructure

Bicep templates in `deploy/bicep/`. Resources:
- App Service Plan (B1 Linux)
- Azure SQL Serverless
- Key Vault (RBAC, 90-day soft delete, purge protection)
- Azure Service Bus Standard (queues + topic)
- Application Insights + OpenTelemetry
- Static Web App (React SPA)

### First Deployment

```bash
# 1. Deploy infrastructure
az deployment group create \
  --resource-group rg-tracer-test \
  --template-file deploy/bicep/main.bicep \
  --parameters deploy/bicep/main.bicepparam

# 2. Store secrets in Key Vault
az keyvault secret set --vault-name tracer-test-kv \
  --name "ConnectionStrings--TracerDb" --value "<connection-string>"
# (see deploy/DEPLOYMENT.md for full list)

# 3. Apply database migrations
cd src/Tracer.Api
dotnet ef database update --connection "<AZURE_SQL_CONNECTION_STRING>"

# 4. Run smoke tests
./deploy/scripts/smoke-test-phase2.sh https://tracer-test-api.azurewebsites.net <API_KEY>
```

## Adding a New Provider

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for step-by-step instructions.

## License

Proprietary — xTuning s.r.o.
