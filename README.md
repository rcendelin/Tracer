# Tracer — Company Data Enrichment Engine

Standalone .NET 10 microservice that enriches partial company information into comprehensive profiles using free public data sources.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- SQL Server (LocalDB, Docker, or Azure SQL)

### Backend

```bash
# Restore and build
dotnet restore
dotnet build

# Configure user secrets (first time only)
cd src/Tracer.Api
dotnet user-secrets set "ConnectionStrings:TracerDb" "Server=(localdb)\mssqllocaldb;Database=TracerDev;Trusted_Connection=true;MultipleActiveResultSets=true"
dotnet user-secrets set "Providers:GoogleMaps:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Providers:AzureMaps:SubscriptionKey" "YOUR_KEY"

# Run API
dotnet run
# API available at https://localhost:7100
```

### Frontend

```bash
cd src/Tracer.Web
npm install
npm run dev
# UI available at http://localhost:5173
```

### Run Tests

```bash
dotnet test
```

## Architecture

Clean Architecture with 4 layers:

```
Tracer.Api           → Minimal API endpoints, middleware
Tracer.Application   → CQRS handlers, orchestrator, scoring, merging
Tracer.Domain        → Entities, value objects, interfaces, events
Tracer.Infrastructure→ EF Core, providers, HTTP clients
Tracer.Web           → React 19 SPA (Vite, TanStack Query, Tailwind)
```

## Enrichment Providers

| Provider | Region | Priority | Data |
|----------|--------|----------|------|
| ARES | CZ/SK | 10 | Registration, legal form, address, VAT |
| GLEIF LEI | Global | 30 | Legal name, addresses, parent company |
| Google Maps | Global | 50 | Address, phone, website, GPS |
| Azure Maps | Global | 50 | Geocoding, standardized address |

## API Endpoints

```
POST  /api/trace                    Submit enrichment request
GET   /api/trace/{traceId}          Get trace result
GET   /api/trace?page&pageSize&...  List traces (paged)
GET   /api/profiles                 List CKB profiles
GET   /api/profiles/{id}            Get profile + recent changes
GET   /api/profiles/{id}/history    Change history + validations
GET   /api/stats                    Dashboard statistics
GET   /health                       Health check
```

## Deployment

See `deploy/` for Bicep IaC and `.github/workflows/` for CI/CD pipelines.

### First Deployment

```bash
# 1. Deploy infrastructure
az deployment group create \
  --resource-group rg-tracer-test \
  --template-file deploy/bicep/main.bicep \
  --parameters environment=test sqlAdminPassword=<PASSWORD>

# 2. Run EF Core migration
cd src/Tracer.Api
dotnet ef database update --connection "<AZURE_SQL_CONNECTION_STRING>"

# 3. Run smoke test
./deploy/scripts/smoke-test.sh https://tracer-test-api.azurewebsites.net
```

## Configuration

| Key | Description | Required |
|-----|-------------|----------|
| `ConnectionStrings:TracerDb` | SQL Server connection string | Yes |
| `Providers:GoogleMaps:ApiKey` | Google Maps Places API key | Yes |
| `Providers:AzureMaps:SubscriptionKey` | Azure Maps subscription key | Yes |
| `Auth:ApiKeys` | Array of valid API keys | Production |
| `Cors:AllowedOrigins` | Allowed CORS origins | Production |

## License

Proprietary — xTuning s.r.o.
