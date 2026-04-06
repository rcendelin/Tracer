@description('App Service Plan + Web App for Tracer API')
param location string = resourceGroup().location
param namePrefix string
param appInsightsInstrumentationKey string = ''
param tags object = {}

var planName = '${namePrefix}-plan'
var appName = '${namePrefix}-api'

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  tags: tags
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsInstrumentationKey
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
      ]
    }
  }
}

output appServiceName string = webApp.name
output appServiceHostName string = webApp.properties.defaultHostName
output appServicePlanId string = appServicePlan.id
