@description('App Service Plan + Web App for Tracer API')
param location string = resourceGroup().location
param namePrefix string
param appInsightsInstrumentationKey string = ''
param keyVaultUri string = ''
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
  identity: {
    type: 'SystemAssigned'
  }
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
        // Connection strings injected from Key Vault via @Microsoft.KeyVault() reference
        // Requires Key Vault RBAC: App Service identity → Key Vault Secrets User
        {
          name: 'ConnectionStrings__TracerDb'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/ConnectionStrings--TracerDb/)'
        }
        {
          name: 'ConnectionStrings__ServiceBus'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/ConnectionStrings--ServiceBus/)'
        }
        {
          name: 'Providers__GoogleMaps__ApiKey'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/Providers--GoogleMaps--ApiKey/)'
        }
        {
          name: 'Providers__AzureMaps__SubscriptionKey'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/Providers--AzureMaps--SubscriptionKey/)'
        }
        {
          name: 'Providers__CompaniesHouse__ApiKey'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/Providers--CompaniesHouse--ApiKey/)'
        }
        {
          name: 'Providers__AbnLookup__Guid'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/Providers--AbnLookup--Guid/)'
        }
        {
          name: 'Auth__ApiKeys__0'
          value: empty(keyVaultUri) ? '' : '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/Auth--ApiKeys--0/)'
        }
      ]
    }
  }
}

output appServiceName string = webApp.name
output appServiceHostName string = webApp.properties.defaultHostName
output appServicePlanId string = appServicePlan.id
@description('System-assigned managed identity principal ID — use for Key Vault RBAC assignment')
output principalId string = webApp.identity.principalId
