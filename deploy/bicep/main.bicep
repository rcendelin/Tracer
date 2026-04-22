@description('Tracer Infrastructure — Phase 2 Async + Sources')
targetScope = 'resourceGroup'

@description('Environment name (test, prod)')
param environment string = 'test'

@description('Azure region')
param location string = resourceGroup().location

@description('SQL admin login')
param sqlAdminLogin string = 'tracerAdmin'

@description('SQL admin password')
@secure()
param sqlAdminPassword string

var namePrefix = 'tracer-${environment}'
var tags = {
  project: 'tracer'
  environment: environment
  managedBy: 'bicep'
}

// Application Insights
module appInsights 'modules/app-insights.bicep' = {
  name: 'appInsights'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// SQL Server + Serverless Database
module sqlServer 'modules/sql-server.bicep' = {
  name: 'sqlServer'
  params: {
    location: location
    namePrefix: namePrefix
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    tags: tags
  }
}

// Key Vault — creates vault first (no RBAC dependency on App Service yet)
module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// App Service (API) — uses Key Vault URI for @Microsoft.KeyVault() app settings references
module appService 'modules/app-service.bicep' = {
  name: 'appService'
  params: {
    location: location
    namePrefix: namePrefix
    appInsightsInstrumentationKey: appInsights.outputs.connectionString
    keyVaultUri: keyVault.outputs.vaultUri
    tags: tags
  }
}

// Key Vault RBAC — Grant App Service managed identity Key Vault Secrets User role
// Use 'existing' reference to scope role assignment to the specific vault (least privilege)
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
resource existingKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVault.outputs.vaultName
}
resource appServiceKeyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.outputs.vaultId, appService.outputs.principalId, keyVaultSecretsUserRoleId)
  scope: existingKeyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: appService.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Static Web App (React SPA)
module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'staticWebApp'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// Service Bus (queues + change notification topic)
module serviceBus 'modules/service-bus.bicep' = {
  name: 'serviceBus'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// Azure Cache for Redis (B-79) — Basic C0, TLS-only, used by IDistributedCache.
module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// Persist the Redis connection string into Key Vault as ConnectionStrings--Redis.
// The double-dash → colon translation is performed by App Service, so the .NET app
// reads it from configuration as ConnectionStrings:Redis. The secret value is never
// echoed in deployment history because the source output is @secure().
resource redisConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: existingKeyVault
  name: 'ConnectionStrings--Redis'
  properties: {
    value: redis.outputs.connectionString
    contentType: 'text/plain'
  }
}

// Outputs — sensitive values (App Insights connection string, SQL FQDN) are NOT exported
// to avoid exposure in deployment history. Retrieve them from Key Vault or Azure Portal.
output apiUrl string = 'https://${appService.outputs.appServiceHostName}'
output webUrl string = 'https://${staticWebApp.outputs.staticWebAppHostName}'
output keyVaultUri string = keyVault.outputs.vaultUri
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output redisCacheName string = redis.outputs.cacheName
