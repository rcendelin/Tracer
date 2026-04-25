@description('Azure Cache for Redis (B-79). Basic C0 = 250 MB, single node, no SLA — sufficient for warm-cache duty in Phase 4.')
param location string = resourceGroup().location

@description('Naming prefix (e.g. tracer-test)')
param namePrefix string

@description('Resource tags')
param tags object = {}

@description('SKU name (Basic | Standard | Premium). Default: Basic.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Basic'

@description('SKU family (C = Basic/Standard, P = Premium).')
@allowed([
  'C'
  'P'
])
param skuFamily string = 'C'

@description('SKU capacity. C0 = 250 MB, C1 = 1 GB, C2 = 2.5 GB, etc.')
@minValue(0)
@maxValue(6)
param skuCapacity int = 0

var cacheName = '${namePrefix}-redis'

resource redisCache 'Microsoft.Cache/redis@2024-03-01' = {
  name: cacheName
  location: location
  tags: tags
  properties: {
    sku: {
      name: skuName
      family: skuFamily
      capacity: skuCapacity
    }
    // Force TLS-only access. The non-SSL port is a frequent source of
    // accidental plaintext credentials; keep it disabled in every env.
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    // Public network access is required by the Basic SKU. Use Premium +
    // VNet injection for production hardening; tracked as a follow-up
    // when traffic justifies the cost step.
    publicNetworkAccess: 'Enabled'
    redisConfiguration: {
      // Reasonable defaults — eviction strategy chosen so cache misses
      // simply re-enrich rather than crashing on memory pressure.
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

@description('Redis connection string (StackExchange.Redis format) — includes primary access key. Marked @secure() so it is not echoed in deployment history.')
@secure()
output connectionString string = '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'

@description('Redis hostname (no port, no key) — useful for diagnostics / metrics.')
output hostName string = redisCache.properties.hostName

@description('Redis resource name (used by main.bicep to write the connection string secret to Key Vault).')
output cacheName string = redisCache.name
