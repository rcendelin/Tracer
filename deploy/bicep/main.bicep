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

@description('Whether to provision Azure OpenAI for Phase 3 AI workloads (B-78). Test envs typically opt out; production opts in once a quota approval is in place.')
param enableAzureOpenAI bool = false

@description('Public network access on the Azure OpenAI account when provisioned. Switch to "Disabled" once VNet integration / private endpoints land (B-92).')
@allowed(['Enabled', 'Disabled'])
param azureOpenAIPublicNetworkAccess string = 'Enabled'

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

// Azure OpenAI (B-78, optional) — provisions the GPT-4o-mini deployments used by
// the AiExtractor (B-57) and LlmDisambiguator (B-64). Default off in test envs;
// flip enableAzureOpenAI=true in main-prod.bicepparam when quota is approved.
module azureOpenAI 'modules/azure-openai.bicep' = if (enableAzureOpenAI) {
  name: 'azureOpenAI'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    publicNetworkAccess: azureOpenAIPublicNetworkAccess
  }
}

// AOAI secrets in Key Vault. Endpoint is non-secret (@Microsoft.KeyVault() ref still
// works either way and keeps configuration consistent across environments). Deployment
// names are non-secret and exposed as plain App Settings. The API key is only persisted
// while we remain pre-managed-identity; once VNet integration lands (B-92) we switch
// to RBAC ('Cognitive Services OpenAI User' on App Service identity) and the apiKey
// secret can be retired.
resource aoaiEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableAzureOpenAI) {
  parent: existingKeyVault
  name: 'Providers--AzureOpenAI--Endpoint'
  properties: {
    value: enableAzureOpenAI ? azureOpenAI!.outputs.endpoint : ''
    contentType: 'text/plain'
  }
}

resource aoaiApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableAzureOpenAI) {
  parent: existingKeyVault
  name: 'Providers--AzureOpenAI--ApiKey'
  properties: {
    value: enableAzureOpenAI ? azureOpenAI!.outputs.apiKey : ''
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
output azureOpenAIEnabled bool = enableAzureOpenAI
output azureOpenAIEndpoint string = enableAzureOpenAI ? azureOpenAI!.outputs.endpoint : ''
output azureOpenAIExtractorDeployment string = enableAzureOpenAI ? azureOpenAI!.outputs.extractorDeploymentName : ''
output azureOpenAIDisambiguatorDeployment string = enableAzureOpenAI ? azureOpenAI!.outputs.disambiguatorDeploymentName : ''
