@description('Tracer Infrastructure — Phase 1 MVP')
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

// Key Vault
module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// App Service (API)
module appService 'modules/app-service.bicep' = {
  name: 'appService'
  params: {
    location: location
    namePrefix: namePrefix
    appInsightsInstrumentationKey: appInsights.outputs.connectionString
    tags: tags
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

// Outputs
output apiUrl string = 'https://${appService.outputs.appServiceHostName}'
output webUrl string = 'https://${staticWebApp.outputs.staticWebAppHostName}'
output sqlServerFqdn string = sqlServer.outputs.serverFqdn
output keyVaultUri string = keyVault.outputs.vaultUri
output appInsightsConnectionString string = appInsights.outputs.connectionString
