// Azure OpenAI account + GPT-4o-mini deployments for Tracer Phase 3 (B-78).
// One Cognitive Services account, two model deployments: extractor (B-57)
// and disambiguator (B-64). Public network access defaults to Disabled;
// production grants `Cognitive Services OpenAI User` to the App Service
// managed identity instead of using API keys.

@description('Azure region.')
param location string

@description('Resource name prefix (e.g. tracer-prod).')
param namePrefix string

@description('Resource tags to propagate.')
param tags object

@description('Soft-deletion retention (days). Cognitive Services minimum is 7 days.')
@minValue(7)
@maxValue(90)
param softDeleteRetentionDays int = 7

@description('GPT-4o-mini model name.')
param modelName string = 'gpt-4o-mini'

@description('GPT-4o-mini model version (Azure OpenAI catalogue).')
param modelVersion string = '2024-07-18'

@description('Capacity for the AiExtractor deployment, in thousands of TPM.')
@minValue(10)
@maxValue(1000)
param extractorCapacity int = 50

@description('Capacity for the LlmDisambiguator deployment, in thousands of TPM.')
@minValue(10)
@maxValue(1000)
param disambiguatorCapacity int = 30

@description('Public network access. "Disabled" requires VNet integration on the consumer side; default keeps key-based access while we remain pre-VNet (private endpoints are a B-92 follow-up).')
@allowed(['Enabled', 'Disabled'])
param publicNetworkAccess string = 'Enabled'

var accountName = '${namePrefix}-aoai'
var customSubdomain = replace(replace(toLower(accountName), '_', '-'), ' ', '-')

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: customSubdomain
    publicNetworkAccess: publicNetworkAccess
    networkAcls: {
      defaultAction: publicNetworkAccess == 'Disabled' ? 'Deny' : 'Allow'
      ipRules: []
      virtualNetworkRules: []
    }
    disableLocalAuth: false
    restrictOutboundNetworkAccess: false
    apiProperties: {}
    restore: false
  }
}

resource extractorDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: 'extractor'
  sku: {
    name: 'Standard'
    capacity: extractorCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

resource disambiguatorDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: 'disambiguator'
  sku: {
    name: 'Standard'
    capacity: disambiguatorCapacity
  }
  // Sequential dependency: Cognitive Services API rejects concurrent deployment
  // creates on the same account. Chain the second after the first.
  dependsOn: [
    extractorDeployment
  ]
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

@description('Account endpoint, e.g. https://tracer-prod-aoai.openai.azure.com/.')
output endpoint string = account.properties.endpoint

@description('Name of the AiExtractor deployment.')
output extractorDeploymentName string = extractorDeployment.name

@description('Name of the LlmDisambiguator deployment.')
output disambiguatorDeploymentName string = disambiguatorDeployment.name

@description('Cognitive Services account name (for RBAC role assignments outside this module).')
output accountName string = account.name

@description('System-assigned principal id of the account (for granting access to Key Vault, etc.).')
output principalId string = account.identity.principalId

@description('Primary access key. Treat as a secret. Production should prefer managed identity (Cognitive Services OpenAI User role) and stop reading this output.')
@secure()
output apiKey string = account.listKeys().key1
