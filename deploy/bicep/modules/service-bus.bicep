@description('Azure region for the Service Bus namespace')
param location string

@description('Naming prefix (e.g. tracer-test)')
param namePrefix string

@description('Resource tags')
param tags object

var serviceBusName = '${namePrefix}-sb'

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

// Scoped access policy for the Tracer application (Send + Listen only, no Manage)
resource tracerPolicy 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2024-01-01' = {
  parent: serviceBus
  name: 'tracer-app'
  properties: {
    rights: [
      'Send'
      'Listen'
    ]
  }
}

// Queue: inbound enrichment requests from FieldForce
resource requestQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'tracer-request'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
  }
}

// Queue: outbound enrichment results to FieldForce
resource responseQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'tracer-response'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
  }
}

// Topic: change event notifications (Critical/Major changes)
resource changesTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'tracer-changes'
  properties: {
    defaultMessageTimeToLive: 'P14D'
    maxSizeInMegabytes: 1024
  }
}

// Subscription: FieldForce consumes Critical/Major change notifications.
// deadLetteringOnFilterEvaluationExceptions=true so a broken filter parks the
// message in DLQ instead of silently dropping it (no observability otherwise).
resource fieldforceSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: changesTopic
  name: 'fieldforce-changes'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    // defaultRuleDescription is not available — $Default rule is removed by overriding
    // with a named rule that replaces it (see below).
  }
}

// Replace the default $Default rule (1=1) with a severity filter.
// Azure creates $Default automatically; adding a rule with the same name overwrites it.
resource severityFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: fieldforceSubscription
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'Severity = \'Critical\' OR Severity = \'Major\''
    }
  }
}

// Subscription: internal monitoring/observability — receives ALL severities that
// Tracer publishes to the topic (Critical/Major/Minor). Cosmetic changes are
// intentionally never published to the topic (log-only), so monitoring never sees them.
// Higher maxDeliveryCount tolerates transient monitoring-tool outages; TTL matches
// fieldforce subscription so ops have the same retention window.
resource monitoringSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: changesTopic
  name: 'monitoring-changes'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    // No $Default override — Azure's implicit 1=1 TrueFilter matches every message.
  }
}

@description('Service Bus connection string (Send + Listen, scoped to tracer-app policy)')
output connectionString string = listKeys(tracerPolicy.id, tracerPolicy.apiVersion).primaryConnectionString

@description('Service Bus namespace hostname')
output namespaceName string = serviceBus.name
