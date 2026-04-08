# Tracer — Deployment Runbook (Phase 2)

## Prerequisites

- Azure CLI (`az`) ≥ 2.55, logged in: `az login`
- GitHub Actions: OIDC secrets configured (see [GitHub Secrets](#github-secrets))
- Resource group exists: `rg-tracer-test` / `rg-tracer-prod`

## First-time deployment (manual)

### 1. Deploy infrastructure via Bicep

```bash
az deployment group create \
  --resource-group rg-tracer-test \
  --template-file deploy/bicep/main.bicep \
  --parameters deploy/bicep/main.bicepparam \
  --parameters sqlAdminPassword="<your-secure-password>" \
  --parameters environment=test
```

Outputs needed for next steps:

```bash
az deployment group show \
  --resource-group rg-tracer-test \
  --name main \
  --query properties.outputs
```

### 2. Store secrets in Key Vault

```bash
KV="tracer-test-kv"

az keyvault secret set --vault-name $KV \
  --name "ConnectionStrings--TracerDb" \
  --value "<SQL connection string>"

az keyvault secret set --vault-name $KV \
  --name "ConnectionStrings--ServiceBus" \
  --value "<Service Bus connection string>"

az keyvault secret set --vault-name $KV \
  --name "Providers--GoogleMaps--ApiKey" \
  --value "<Google Maps API key>"

az keyvault secret set --vault-name $KV \
  --name "Providers--AzureMaps--SubscriptionKey" \
  --value "<Azure Maps subscription key>"

az keyvault secret set --vault-name $KV \
  --name "Providers--CompaniesHouse--ApiKey" \
  --value "<Companies House API key>"

az keyvault secret set --vault-name $KV \
  --name "Providers--AbnLookup--Guid" \
  --value "<ABN Lookup GUID>"

az keyvault secret set --vault-name $KV \
  --name "Auth--ApiKeys--0" \
  --value "<Tracer API key>"
```

### 3. Run EF Core migrations

After the API is deployed, run migrations against the Azure SQL database:

```bash
cd src/Tracer.Api
dotnet ef database update \
  --connection "<Azure SQL connection string>" \
  --project ../Tracer.Infrastructure \
  --startup-project .
```

### 4. Deploy the API

```bash
dotnet publish src/Tracer.Api/Tracer.Api.csproj \
  --configuration Release --output ./publish/api

az webapp deploy \
  --resource-group rg-tracer-test \
  --name tracer-test-api \
  --src-path ./publish/api \
  --type zip
```

### 5. Deploy the Web SPA

```bash
cd src/Tracer.Web
npm ci && npx vite build

# Deploy via Static Web App CLI or GitHub Actions
```

### 6. Run smoke tests

```bash
API_URL="https://tracer-test-api.azurewebsites.net"
API_KEY="<Tracer API key from Key Vault>"

chmod +x deploy/scripts/smoke-test-phase2.sh
./deploy/scripts/smoke-test-phase2.sh "$API_URL" "$API_KEY"
```

---

## GitHub Actions (automated)

Push to `main` triggers full deployment automatically.

Manual deployment:

1. Go to **Actions** → **Deploy to Azure**
2. Click **Run workflow**
3. Select environment (`test` / `prod`)

### GitHub Secrets

Configure the following secrets in **Settings → Environments → {environment} → Secrets**:

| Secret | Description |
|--------|-------------|
| `AZURE_CLIENT_ID` | Service Principal app ID (OIDC federated) |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `SQL_ADMIN_PASSWORD` | SQL Server admin password |
| `TRACER_DB_CONNECTION_STRING` | Full ADO.NET connection string |
| `SERVICE_BUS_CONNECTION_STRING` | Service Bus connection string (tracer-app policy) |
| `GOOGLE_MAPS_API_KEY` | Google Maps Places API key |
| `AZURE_MAPS_SUBSCRIPTION_KEY` | Azure Maps subscription key |
| `COMPANIES_HOUSE_API_KEY` | Companies House REST API key |
| `ABN_LOOKUP_GUID` | ABN Lookup registration GUID |
| `TRACER_API_KEY` | API key for Tracer REST API auth |
| `SWA_DEPLOYMENT_TOKEN` | Static Web App deployment token |

---

## Service Bus topology

| Resource | Type | Purpose |
|----------|------|---------|
| `tracer-request` | Queue | Inbound enrichment requests from FieldForce |
| `tracer-response` | Queue | Outbound enrichment results to FieldForce |
| `tracer-changes` | Topic | Change event notifications |
| `tracer-changes/fieldforce-changes` | Subscription | FieldForce receives Critical + Major changes |

Severity filter on `fieldforce-changes`:

```sql
Severity = 'Critical' OR Severity = 'Major'
```

---

## Phase 2 smoke test — 20 companies

The `smoke-test-phase2.sh` script tests 20 companies across 5 countries.

| # | Company | Country | Provider |
|---|---------|---------|---------|
| 1 | ŠKODA AUTO a.s. (IČO: 00177041) | CZ | ARES |
| 2 | Kofola ČeskoSlovensko a.s. (IČO: 27605535) | CZ | ARES |
| 3 | Česká spořitelna (IČO: 45244782) | CZ | ARES |
| 4 | ČEZ, a. s. (IČO: 45274649) | CZ | ARES |
| 5 | Tesco PLC | GB | Companies House |
| 6 | HSBC Holdings plc | GB | Companies House |
| 7 | BP p.l.c. | GB | Companies House |
| 8 | Rolls-Royce Holdings plc | GB | Companies House |
| 9 | BHP Group Limited | AU | ABN Lookup |
| 10 | Commonwealth Bank of Australia | AU | ABN Lookup |
| 11 | Woolworths Group Limited | AU | ABN Lookup |
| 12 | Rio Tinto Limited | AU | ABN Lookup |
| 13 | Tesla, Inc. | US | SEC EDGAR |
| 14 | Apple Inc. | US | SEC EDGAR |
| 15 | Microsoft Corporation | US | SEC EDGAR |
| 16 | Amazon.com, Inc. | US | SEC EDGAR |
| 17 | Siemens AG | DE | GLEIF + Google Maps |
| 18 | SAP SE | DE | GLEIF + Google Maps |
| 19 | BMW AG | DE | GLEIF + Google Maps |
| 20 | Volkswagen AG | DE | GLEIF + Google Maps |

---

## Rollback

If the deployment fails, roll back the App Service to the previous slot:

```bash
az webapp deployment slot swap \
  --resource-group rg-tracer-test \
  --name tracer-test-api \
  --slot staging \
  --target-slot production
```

Or redeploy the previous artifact from GitHub Actions.
