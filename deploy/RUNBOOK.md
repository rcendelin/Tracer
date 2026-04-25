# Tracer Deployment Runbook

## First Deployment Checklist

### 1. Prerequisites
- [ ] Azure subscription with Contributor access
- [ ] GitHub repository with secrets configured
- [ ] Google Maps API key (Places API enabled)
- [ ] Azure Maps subscription key
- [ ] SQL admin password generated

### 2. Azure Infrastructure (Bicep)

```bash
# Create resource group
az group create --name rg-tracer-test --location westeurope

# Deploy infrastructure
az deployment group create \
  --resource-group rg-tracer-test \
  --template-file deploy/bicep/main.bicep \
  --parameters environment=test sqlAdminPassword='<STRONG_PASSWORD>'

# Note the outputs: apiUrl, webUrl, sqlServerFqdn, keyVaultUri
```

### 3. Configure Key Vault Secrets

```bash
VAULT_NAME="tracer-test-kv"

az keyvault secret set --vault-name $VAULT_NAME --name "ConnectionStrings--TracerDb" \
  --value "Server=tcp:<SQL_FQDN>,1433;Database=TracerDb;User ID=tracerAdmin;Password=<PASSWORD>;Encrypt=true;"

az keyvault secret set --vault-name $VAULT_NAME --name "Providers--GoogleMaps--ApiKey" \
  --value "<GOOGLE_MAPS_KEY>"

az keyvault secret set --vault-name $VAULT_NAME --name "Providers--AzureMaps--SubscriptionKey" \
  --value "<AZURE_MAPS_KEY>"

az keyvault secret set --vault-name $VAULT_NAME --name "Auth--ApiKeys--0" \
  --value "<GENERATED_API_KEY>"
```

### 4. Configure App Service

```bash
APP_NAME="tracer-test-api"

# Key Vault reference for connection string
az webapp config connection-string set --resource-group rg-tracer-test --name $APP_NAME \
  --connection-string-type SQLAzure \
  --settings TracerDb="@Microsoft.KeyVault(VaultName=$VAULT_NAME;SecretName=ConnectionStrings--TracerDb)"

# CORS
az webapp cors add --resource-group rg-tracer-test --name $APP_NAME \
  --allowed-origins "https://tracer-test-web.azurestaticapps.net"
```

### 5. EF Core Migration

```bash
cd src/Tracer.Api
dotnet ef database update --connection "<DIRECT_SQL_CONNECTION_STRING>"
```

### 6. GitHub Actions Secrets

Configure in GitHub repo Settings → Secrets:
- `AZURE_CLIENT_ID` — Service principal client ID
- `AZURE_TENANT_ID` — Azure AD tenant ID
- `AZURE_SUBSCRIPTION_ID` — Azure subscription ID
- `SWA_DEPLOYMENT_TOKEN` — Static Web App deployment token

### 7. Deploy & Verify

```bash
# Push to main triggers deploy, or manual dispatch
gh workflow run deploy.yml --ref main -f environment=test

# Run smoke test
./deploy/scripts/smoke-test.sh https://tracer-test-api.azurewebsites.net <API_KEY>
```

### 8. Post-Deployment Verification

- [ ] Health check returns 200
- [ ] POST /api/trace returns 201 with enriched data
- [ ] React UI loads and displays dashboard
- [ ] Application Insights shows requests and traces
- [ ] SQL database has TraceRequests and CompanyProfiles tables
- [ ] API key auth works (401 without key, 200 with key)

## Rollback

```bash
# Redeploy previous version
gh run rerun <PREVIOUS_RUN_ID>

# Or deploy specific commit
git checkout <SAFE_COMMIT>
gh workflow run deploy.yml --ref HEAD
```
