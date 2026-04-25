@description('Azure Static Web App for React SPA')
param location string = resourceGroup().location
param namePrefix string
param tags object = {}

var swaName = '${namePrefix}-web'

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: swaName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
  }
}

output staticWebAppName string = staticWebApp.name
output staticWebAppHostName string = staticWebApp.properties.defaultHostname
output deploymentToken string = staticWebApp.listSecrets().properties.apiKey
