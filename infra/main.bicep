targetScope = 'resourceGroup'

metadata name = 'GitHub Copilot Budget Manager - Azure Commercial'
metadata description = 'Customer-owned Azure Commercial deployment for one GitHub Enterprise Cloud enterprise.'

@description('Short environment name used in resource names and tags, for example dev, test, or prod.')
@minLength(2)
@maxLength(12)
param environmentName string

@description('Azure region for regional resources.')
param location string = resourceGroup().location

@description('Azure region for Static Web Apps. This can differ from the primary region when service availability requires it.')
param staticWebAppLocation string = location

@description('Microsoft Entra tenant ID used by API authentication and SQL administration.')
param tenantId string = subscription().tenantId

@description('Stable application enterprise ID. This is an application-owned GUID, not the numeric GitHub enterprise identifier.')
@minLength(36)
@maxLength(36)
param enterpriseId string

@description('GitHub Enterprise Cloud enterprise slug.')
@minLength(1)
param enterpriseSlug string

@description('GitHub App client ID or app ID used as the JWT issuer.')
@minLength(1)
param githubAppIssuer string

@description('GitHub App installation ID for the enterprise installation.')
@minValue(1)
param githubAppInstallationId int

@secure()
@description('Optional GitHub App PEM private key. Leave empty to upload the github-app-private-key Key Vault secret after deployment.')
param githubAppPrivateKey string = ''

@description('Microsoft Entra object ID of the user, group, or service principal that administers Azure SQL.')
@minLength(36)
@maxLength(36)
param sqlAdministratorObjectId string

@description('Display name of the Microsoft Entra principal configured as the Azure SQL administrator.')
@minLength(1)
param sqlAdministratorLogin string

@allowed([
  'Application'
  'Group'
  'User'
])
@description('Principal type of the Microsoft Entra Azure SQL administrator.')
param sqlAdministratorPrincipalType string = 'Group'

@description('Application ID URI or client ID expected in API access tokens.')
@minLength(1)
param apiAudience string

@description('Microsoft Entra SPA application client ID used by the dashboard PKCE flow.')
@minLength(36)
@maxLength(36)
param dashboardClientId string

@description('Delegated API scope requested by the dashboard, for example api://<api-client-id>/access_as_user.')
@minLength(1)
param dashboardApiScope string

@description('Microsoft Graph extension or user property containing each user GitHub login.')
@minLength(1)
param graphGitHubLoginProperty string

@description('Comma-separated allowlist for GitHub signed report download hosts.')
@minLength(1)
param signedReportAllowedHosts string = 'github.com,objects.githubusercontent.com,github-releases.githubusercontent.com'

@description('Retention in days for raw GitHub reports in Blob Storage. This must be selected for each environment.')
@minValue(7)
@maxValue(3650)
param rawReportRetentionDays int

@description('Blob soft-delete and version retention in days.')
@minValue(7)
@maxValue(365)
param blobRecoveryRetentionDays int = 30

@description('Log Analytics retention in days.')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 90

@description('Azure SQL point-in-time restore retention in days.')
@minValue(7)
@maxValue(35)
param sqlPointInTimeRetentionDays int = 14

@description('Azure SQL long-term monthly backup retention in ISO 8601 duration format, for example P6M.')
param sqlMonthlyBackupRetention string = 'P6M'

@allowed([
  'Geo'
  'GeoZone'
  'Local'
  'Zone'
])
@description('Backup storage redundancy for Azure SQL Database.')
param sqlBackupStorageRedundancy string = 'Geo'

@allowed([
  'GeneralPurposeServerless'
  'GeneralPurposeProvisioned'
])
@description('Azure SQL compute model.')
param sqlComputeModel string = 'GeneralPurposeServerless'

@description('Azure SQL database maximum size in bytes.')
@minValue(1073741824)
param sqlMaxSizeBytes int = 34359738368

@description('Azure SQL provisioned or serverless maximum vCores.')
@minValue(1)
param sqlMaxVcores int = 2

@description('Azure SQL serverless minimum vCores. Ignored for provisioned compute.')
param sqlMinVcores string = '0.5'

@description('Azure SQL serverless auto-pause delay in minutes. Ignored for provisioned compute; -1 disables auto-pause.')
@minValue(-1)
param sqlAutoPauseDelayMinutes int = 60

@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_GZRS'
  'Standard_RAGRS'
  'Standard_RAGZRS'
  'Standard_ZRS'
])
@description('Storage account replication SKU.')
param storageSku string = 'Standard_GRS'

@allowed([
  'Standard'
  'Premium'
])
@description('Service Bus tier. Premium is required when private endpoints are enabled.')
param serviceBusSku string = 'Premium'

@description('Deploy private endpoints and disable public access for supported data services. Service Bus must use Premium.')
param enablePrivateEndpoints bool = true

@description('Enable availability-zone-aware SKUs where the selected region and subscription support them.')
param enableZoneRedundancy bool = false

@description('Deploy the API and worker jobs. Leave false for the first provision, then enable after images exist and the GitHub App key is in Key Vault.')
param deployApplicationResources bool = false

@allowed([
  'Standard'
  'Premium'
])
@description('Container Registry SKU when private endpoints are disabled. Private endpoint deployments always use Premium.')
param containerRegistrySku string = 'Standard'

@allowed([
  'Free'
  'Standard'
])
@description('Static Web App SKU. Standard is required for backend linkage to the Container App API.')
param staticWebAppSku string = 'Standard'

@description('Virtual network address space.')
param virtualNetworkAddressPrefix string = '10.42.0.0/16'

@description('Dedicated Container Apps environment infrastructure subnet.')
param containerAppsSubnetPrefix string = '10.42.0.0/23'

@description('Subnet used by private endpoints.')
param privateEndpointSubnetPrefix string = '10.42.2.0/24'

@description('Session-enabled Service Bus queue used for workflow events.')
param serviceBusQueueName string = 'workflow-events'

@description('Default workflow event message TTL in ISO 8601 duration format.')
param serviceBusMessageTimeToLive string = 'P14D'

@description('Duplicate detection history window in ISO 8601 duration format.')
param serviceBusDuplicateDetectionWindow string = 'PT10M'

@description('Delivery attempts before Service Bus moves a message to the dead-letter queue.')
@minValue(1)
param serviceBusMaxDeliveryCount int = 10

@description('Microsoft Teams team ID used by the disabled notification skeleton after connector authorization.')
param teamsTeamId string = ''

@description('Microsoft Teams channel ID used by the disabled notification skeleton after connector authorization.')
param teamsChannelId string = ''

@description('Semicolon-separated Outlook recipients used by the disabled notification skeleton after connector authorization.')
param notificationRecipients string = ''

@allowed([
  'Outlook'
  'Teams;Outlook'
])
@description('Delivery records created by notification planning. Use Outlook for an email-only deployment.')
param notificationChannels string = 'Teams;Outlook'

@description('Publish budget lifecycle events to the transactional outbox. Disable for budget-only deployments without a workflow dispatcher.')
param publishBudgetLifecycleEvents bool = true

@description('Maximum currency-unit increase permitted by one budget proposal. Set explicitly for the customer budget scale.')
@minValue(0)
param budgetMaximumIncreaseAmount int

@description('Maximum percentage increase permitted by one budget proposal.')
@minValue(0)
@maxValue(100)
param budgetMaximumIncreasePercent int

@description('Maximum cumulative currency-unit increase permitted per budget during a month.')
@minValue(0)
param budgetMaximumCumulativeMonthlyIncrease int

@description('Required percentage headroom above the forecast.')
@minValue(0)
@maxValue(100)
param budgetForecastHeadroomPercent int

@description('Minimum hours between tool-applied increases to the same budget.')
@minValue(0)
param budgetCooldownHours int

@description('Maximum age in hours of financial inputs used for a proposal.')
@minValue(1)
param budgetMaximumDataAgeHours int

@description('Hours before an unapproved budget proposal expires.')
@minValue(1)
@maxValue(168)
param budgetApprovalLifetimeHours int = 48

@description('Allow approved requests to reach the GitHub budget PATCH operation. Defaults off; when false, the executor job is manual and the Worker rejects execution.')
param budgetWritesEnabled bool = false

@description('API container image tag published to the deployment ACR.')
param apiImageTag string = 'latest'

@description('Worker container image tag published to the deployment ACR.')
param workerImageTag string = 'latest'

@description('API minimum replica count.')
@minValue(1)
param apiMinReplicas int = 1

@description('API maximum replica count.')
@minValue(1)
param apiMaxReplicas int = 5

@description('API container CPU cores.')
param apiCpu string = '0.5'

@description('API container memory.')
param apiMemory string = '1Gi'

@description('Worker job container CPU cores.')
param workerCpu string = '0.5'

@description('Worker job container memory.')
param workerMemory string = '1Gi'

@description('Maximum worker execution duration in seconds.')
@minValue(300)
param workerReplicaTimeoutSeconds int = 3600

@description('Cron schedule for budget synchronization in UTC. Defaults to every four hours.')
param syncBudgetsSchedule string = '17 */4 * * *'

@description('Cron schedule for finalized T-3 report ingestion in UTC.')
param ingestDailySchedule string = '23 3 * * *'

@description('Cron schedule for Microsoft Entra identity synchronization in UTC.')
param syncIdentitiesSchedule string = '53 3 * * *'

@description('Cron schedule for classification in UTC, set after ingestion.')
param classifySchedule string = '23 5 * * *'

@description('Cron schedule for health notification planning in UTC, set after classification.')
param planNotificationsSchedule string = '38 5 * * *'

@description('Cron schedule for forecasting in UTC.')
param forecastSchedule string = '53 5 * * *'

@description('Cron schedule for monthly baseline reconciliation in UTC.')
param reconcileBaselinesSchedule string = '17 6 1 * *'

@description('Cron schedule for retention processing in UTC.')
param applyRetentionSchedule string = '47 6 * * *'

@description('Cron schedule for approved budget execution in UTC. A manual start remains available.')
param executeApprovedSchedule string = '*/5 * * * *'

@description('Cron schedule for SQL outbox dispatch in UTC.')
param dispatchOutboxSchedule string = '*/2 * * * *'

@description('Additional tags applied to every taggable resource.')
param tags object = {}

var compactEnvironmentName = toLower(replace(replace(environmentName, '-', ''), '_', ''))
var deploymentToken = take(uniqueString(subscription().id, resourceGroup().id, environmentName), 8)
var workloadName = 'copilot-budget-manager'
var commonTags = union(tags, {
  application: workloadName
  'azd-env-name': environmentName
  environment: environmentName
  managedBy: 'azd-bicep'
  dataBoundary: 'customer-owned'
})

var names = {
  runtimeIdentity: 'id-cbm-runtime-${environmentName}-${deploymentToken}'
  migrationIdentity: 'id-cbm-migrate-${environmentName}-${deploymentToken}'
  notificationIdentity: 'id-cbm-notify-${environmentName}-${deploymentToken}'
  containerRegistry: take('crcbm${compactEnvironmentName}${deploymentToken}', 50)
  containerAppsEnvironment: 'cae-cbm-${environmentName}-${deploymentToken}'
  containerAppsInfrastructureResourceGroup: take('me-cbm-${environmentName}-${deploymentToken}', 63)
  api: 'ca-cbm-api-${environmentName}-${deploymentToken}'
  staticWebApp: 'stapp-cbm-${environmentName}-${deploymentToken}'
  sqlServer: 'sql-cbm-${environmentName}-${deploymentToken}'
  sqlDatabase: 'sqldb-copilot-budget-manager'
  storageAccount: take('stcbm${compactEnvironmentName}${deploymentToken}', 24)
  serviceBusNamespace: take('sb-cbm-${environmentName}-${deploymentToken}', 50)
  keyVault: take('kv-cbm-${environmentName}-${deploymentToken}', 24)
  logAnalytics: 'log-cbm-${environmentName}-${deploymentToken}'
  applicationInsights: 'appi-cbm-${environmentName}-${deploymentToken}'
  logicApp: 'logic-cbm-notify-${environmentName}-${deploymentToken}'
  virtualNetwork: 'vnet-cbm-${environmentName}-${deploymentToken}'
}

var effectiveServiceBusSku = enablePrivateEndpoints ? 'Premium' : serviceBusSku
var effectiveStaticWebAppSku = deployApplicationResources ? 'Standard' : staticWebAppSku
var effectiveApiMaxReplicas = max(apiMinReplicas, apiMaxReplicas)

var blobPrivateDnsZoneName = 'privatelink.blob.${environment().suffixes.storage}'
var sqlPrivateDnsZoneName = 'privatelink.${environment().suffixes.sqlServerHostname}'
var privateDnsZoneNames = [
  'privatelink.azurecr.io'
  blobPrivateDnsZoneName
  sqlPrivateDnsZoneName
  'privatelink.servicebus.windows.net'
  'privatelink.vaultcore.azure.net'
]
var containerAppsSubnetResourceId = resourceId(
  'Microsoft.Network/virtualNetworks/subnets',
  names.virtualNetwork,
  'container-apps'
)
var privateEndpointSubnetResourceId = resourceId(
  'Microsoft.Network/virtualNetworks/subnets',
  names.virtualNetwork,
  'private-endpoints'
)
var containerRegistryPrivateDnsZoneResourceId = resourceId(
  'Microsoft.Network/privateDnsZones',
  'privatelink.azurecr.io'
)
var blobPrivateDnsZoneResourceId = resourceId(
  'Microsoft.Network/privateDnsZones',
  blobPrivateDnsZoneName
)
var sqlPrivateDnsZoneResourceId = resourceId(
  'Microsoft.Network/privateDnsZones',
  sqlPrivateDnsZoneName
)
var serviceBusPrivateDnsZoneResourceId = resourceId(
  'Microsoft.Network/privateDnsZones',
  'privatelink.servicebus.windows.net'
)
var keyVaultPrivateDnsZoneResourceId = resourceId(
  'Microsoft.Network/privateDnsZones',
  'privatelink.vaultcore.azure.net'
)
var sqlDatabases = sqlComputeModel == 'GeneralPurposeServerless'
  ? [
      {
        autoPauseDelay: sqlAutoPauseDelayMinutes
        availabilityZone: -1
        backupLongTermRetentionPolicy: {
          monthlyRetention: sqlMonthlyBackupRetention
        }
        backupShortTermRetentionPolicy: {
          retentionDays: sqlPointInTimeRetentionDays
        }
        diagnosticSettings: [
          {
            name: 'send-to-log-analytics'
            workspaceResourceId: logAnalytics.outputs.resourceId
          }
        ]
        maxSizeBytes: sqlMaxSizeBytes
        minCapacity: sqlMinVcores
        name: names.sqlDatabase
        requestedBackupStorageRedundancy: sqlBackupStorageRedundancy
        sku: {
          capacity: sqlMaxVcores
          family: 'Gen5'
          name: 'GP_S_Gen5'
          tier: 'GeneralPurpose'
        }
        zoneRedundant: enableZoneRedundancy
      }
    ]
  : [
      {
        availabilityZone: -1
        backupLongTermRetentionPolicy: {
          monthlyRetention: sqlMonthlyBackupRetention
        }
        backupShortTermRetentionPolicy: {
          retentionDays: sqlPointInTimeRetentionDays
        }
        diagnosticSettings: [
          {
            name: 'send-to-log-analytics'
            workspaceResourceId: logAnalytics.outputs.resourceId
          }
        ]
        maxSizeBytes: sqlMaxSizeBytes
        name: names.sqlDatabase
        requestedBackupStorageRedundancy: sqlBackupStorageRedundancy
        sku: {
          capacity: sqlMaxVcores
          family: 'Gen5'
          name: 'GP_Gen5'
          tier: 'GeneralPurpose'
        }
        zoneRedundant: enableZoneRedundancy
      }
    ]

module runtimeIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.6.0' = {
  name: 'identity-runtime'
  params: {
    location: location
    name: names.runtimeIdentity
    tags: commonTags
  }
}

module migrationIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.6.0' = {
  name: 'identity-migration'
  params: {
    location: location
    name: names.migrationIdentity
    tags: commonTags
  }
}

module notificationIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.6.0' = {
  name: 'identity-notification'
  params: {
    location: location
    name: names.notificationIdentity
    tags: commonTags
  }
}

module network 'br/public:avm/res/network/virtual-network:0.10.2' = {
  name: 'network'
  params: {
    addressPrefixes: [
      virtualNetworkAddressPrefix
    ]
    location: location
    name: names.virtualNetwork
    subnets: [
      {
        addressPrefix: containerAppsSubnetPrefix
        delegation: 'Microsoft.App/environments'
        name: 'container-apps'
      }
      {
        addressPrefix: privateEndpointSubnetPrefix
        name: 'private-endpoints'
        privateEndpointNetworkPolicies: 'Disabled'
      }
    ]
    tags: commonTags
  }
}

resource privateDnsZones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for zoneName in privateDnsZoneNames: if (enablePrivateEndpoints) {
  name: zoneName
  location: 'global'
  tags: commonTags
}]

resource privateDnsZoneVnetLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (zoneName, index) in privateDnsZoneNames: if (enablePrivateEndpoints) {
  parent: privateDnsZones[index]
  name: 'link-${deploymentToken}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.outputs.resourceId
    }
  }
}]

module logAnalytics 'br/public:avm/res/operational-insights/workspace:0.16.1' = {
  name: 'log-analytics'
  params: {
    dataRetention: logRetentionDays
    features: {
      disableLocalAuth: true
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    location: location
    name: names.logAnalytics
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    skuName: 'PerGB2018'
    tags: commonTags
  }
}

module applicationInsights 'br/public:avm/res/insights/component:0.8.0' = {
  name: 'application-insights'
  params: {
    applicationType: 'web'
    disableLocalAuth: true
    disableIpMasking: false
    kind: 'web'
    location: location
    name: names.applicationInsights
    roleAssignments: [
      {
        principalId: runtimeIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Monitoring Metrics Publisher'
      }
    ]
    tags: commonTags
    workspaceResourceId: logAnalytics.outputs.resourceId
  }
}

module containerRegistry 'br/public:avm/res/container-registry/registry:0.13.0' = {
  name: 'container-registry'
  params: {
    acrAdminUserEnabled: false
    acrSku: enablePrivateEndpoints ? 'Premium' : containerRegistrySku
    anonymousPullEnabled: false
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalytics.outputs.resourceId
      }
    ]
    exportPolicyStatus: 'disabled'
    location: location
    name: names.containerRegistry
    networkRuleBypassAllowedForTasks: true
    networkRuleBypassOptions: 'AzureServices'
    privateEndpoints: enablePrivateEndpoints
      ? [
          {
            name: 'pep-${names.containerRegistry}'
            privateDnsZoneGroup: {
              privateDnsZoneGroupConfigs: [
                {
                  privateDnsZoneResourceId: containerRegistryPrivateDnsZoneResourceId
                }
              ]
            }
            service: 'registry'
            subnetResourceId: privateEndpointSubnetResourceId
            tags: commonTags
          }
        ]
      : []
    publicNetworkAccess: enablePrivateEndpoints ? 'Disabled' : 'Enabled'
    retentionPolicyDays: 30
    retentionPolicyStatus: 'enabled'
    roleAssignmentMode: 'LegacyRegistryPermissions'
    roleAssignments: [
      {
        principalId: runtimeIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'AcrPull'
      }
      {
        principalId: migrationIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'AcrPull'
      }
    ]
    tags: commonTags
    zoneRedundancy: enableZoneRedundancy ? 'Enabled' : 'Disabled'
  }
  dependsOn: [
    privateDnsZoneVnetLinks
  ]
}

module keyVault 'br/public:avm/res/key-vault/vault:0.14.0' = {
  name: 'key-vault'
  params: {
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalytics.outputs.resourceId
      }
    ]
    enablePurgeProtection: true
    enableRbacAuthorization: true
    enableSoftDelete: true
    enableVaultForDeployment: false
    enableVaultForDiskEncryption: false
    enableVaultForTemplateDeployment: false
    location: location
    name: names.keyVault
    networkAcls: {
      bypass: enablePrivateEndpoints ? 'None' : 'AzureServices'
      defaultAction: enablePrivateEndpoints ? 'Deny' : 'Allow'
    }
    privateEndpoints: enablePrivateEndpoints
      ? [
          {
            name: 'pep-${names.keyVault}'
            privateDnsZoneGroup: {
              privateDnsZoneGroupConfigs: [
                {
                  privateDnsZoneResourceId: keyVaultPrivateDnsZoneResourceId
                }
              ]
            }
            service: 'vault'
            subnetResourceId: privateEndpointSubnetResourceId
            tags: commonTags
          }
        ]
      : []
    publicNetworkAccess: enablePrivateEndpoints ? 'Disabled' : 'Enabled'
    roleAssignments: [
      {
        principalId: runtimeIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Key Vault Secrets User'
      }
    ]
    secrets: empty(githubAppPrivateKey)
      ? []
      : [
          {
            contentType: 'application/x-pem-file'
            name: 'github-app-private-key'
            value: githubAppPrivateKey
          }
        ]
    sku: 'standard'
    softDeleteRetentionInDays: 90
    tags: commonTags
  }
  dependsOn: [
    privateDnsZoneVnetLinks
  ]
}

module storage 'br/public:avm/res/storage/storage-account:0.33.0' = {
  name: 'storage'
  params: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: false
    blobServices: {
      containerDeleteRetentionPolicyDays: blobRecoveryRetentionDays
      containerDeleteRetentionPolicyEnabled: true
      containers: [
        {
          name: 'raw-reports'
          publicAccess: 'None'
        }
      ]
      deleteRetentionPolicyDays: blobRecoveryRetentionDays
      deleteRetentionPolicyEnabled: true
      diagnosticSettings: [
        {
          name: 'send-to-log-analytics'
          workspaceResourceId: logAnalytics.outputs.resourceId
        }
      ]
      isVersioningEnabled: true
      versionDeletePolicyDays: blobRecoveryRetentionDays
    }
    defaultToOAuthAuthentication: true
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalytics.outputs.resourceId
      }
    ]
    kind: 'StorageV2'
    location: location
    managementPolicyRules: [
      {
        definition: {
          actions: {
            baseBlob: {
              delete: {
                daysAfterModificationGreaterThan: rawReportRetentionDays
              }
            }
          }
          filters: {
            blobTypes: [
              'blockBlob'
            ]
            prefixMatch: [
              'raw-reports/'
            ]
          }
        }
        enabled: true
        name: 'delete-expired-raw-reports'
        type: 'Lifecycle'
      }
    ]
    minimumTlsVersion: 'TLS1_2'
    name: names.storageAccount
    networkAcls: {
      bypass: enablePrivateEndpoints ? 'None' : 'AzureServices'
      defaultAction: enablePrivateEndpoints ? 'Deny' : 'Allow'
    }
    privateEndpoints: enablePrivateEndpoints
      ? [
          {
            name: 'pep-${names.storageAccount}-blob'
            privateDnsZoneGroup: {
              privateDnsZoneGroupConfigs: [
                {
                  privateDnsZoneResourceId: blobPrivateDnsZoneResourceId
                }
              ]
            }
            service: 'blob'
            subnetResourceId: privateEndpointSubnetResourceId
            tags: commonTags
          }
        ]
      : []
    publicNetworkAccess: enablePrivateEndpoints ? 'Disabled' : 'Enabled'
    requireInfrastructureEncryption: true
    roleAssignments: [
      {
        principalId: runtimeIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Storage Blob Data Contributor'
      }
    ]
    skuName: storageSku
    supportsHttpsTrafficOnly: true
    tags: commonTags
  }
  dependsOn: [
    privateDnsZoneVnetLinks
  ]
}

module serviceBus 'br/public:avm/res/service-bus/namespace:0.17.0' = {
  name: 'service-bus'
  params: {
    authorizationRules: []
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalytics.outputs.resourceId
      }
    ]
    disableLocalAuth: true
    location: location
    minimumTlsVersion: '1.2'
    name: names.serviceBusNamespace
    networkRuleSets: {
      defaultAction: enablePrivateEndpoints ? 'Deny' : 'Allow'
      publicNetworkAccess: 'Enabled'
      trustedServiceAccessEnabled: true
    }
    privateEndpoints: enablePrivateEndpoints
      ? [
          {
            name: 'pep-${names.serviceBusNamespace}'
            privateDnsZoneGroup: {
              privateDnsZoneGroupConfigs: [
                {
                  privateDnsZoneResourceId: serviceBusPrivateDnsZoneResourceId
                }
              ]
            }
            service: 'namespace'
            subnetResourceId: privateEndpointSubnetResourceId
            tags: commonTags
          }
        ]
      : []
    publicNetworkAccess: 'Enabled'
    queues: [
      {
        deadLetteringOnMessageExpiration: true
        defaultMessageTimeToLive: serviceBusMessageTimeToLive
        duplicateDetectionHistoryTimeWindow: serviceBusDuplicateDetectionWindow
        enableBatchedOperations: true
        maxDeliveryCount: serviceBusMaxDeliveryCount
        maxSizeInMegabytes: 1024
        name: serviceBusQueueName
        requiresDuplicateDetection: true
        requiresSession: true
        roleAssignments: [
          {
            principalId: runtimeIdentity.outputs.principalId
            principalType: 'ServicePrincipal'
            roleDefinitionIdOrName: 'Azure Service Bus Data Sender'
          }
          {
            principalId: notificationIdentity.outputs.principalId
            principalType: 'ServicePrincipal'
            roleDefinitionIdOrName: 'Azure Service Bus Data Receiver'
          }
        ]
      }
    ]
    skuObject: effectiveServiceBusSku == 'Premium'
      ? {
          capacity: 1
          name: 'Premium'
        }
      : {
          name: 'Standard'
        }
    tags: commonTags
      zoneRedundant: effectiveServiceBusSku == 'Premium' && enableZoneRedundancy
  }
  dependsOn: [
    privateDnsZoneVnetLinks
  ]
}

module sql 'br/public:avm/res/sql/server:0.22.0' = {
  name: 'sql'
  params: {
    administrators: {
      azureADOnlyAuthentication: true
      login: sqlAdministratorLogin
      principalType: sqlAdministratorPrincipalType
      sid: sqlAdministratorObjectId
      tenantId: tenantId
    }
    auditSettings: {
      isAzureMonitorTargetEnabled: true
      state: 'Enabled'
    }
    connectionPolicy: 'Redirect'
    databases: sqlDatabases
    location: location
    minimalTlsVersion: '1.2'
    name: names.sqlServer
    privateEndpoints: enablePrivateEndpoints
      ? [
          {
            name: 'pep-${names.sqlServer}'
            privateDnsZoneGroup: {
              privateDnsZoneGroupConfigs: [
                {
                  privateDnsZoneResourceId: sqlPrivateDnsZoneResourceId
                }
              ]
            }
            service: 'sqlServer'
            subnetResourceId: privateEndpointSubnetResourceId
            tags: commonTags
          }
        ]
      : []
    publicNetworkAccess: enablePrivateEndpoints ? 'Disabled' : 'Enabled'
    securityAlertPolicies: [
      {
        emailAccountAdmins: true
        name: 'Default'
        state: 'Enabled'
      }
    ]
    tags: commonTags
  }
  dependsOn: [
    privateDnsZoneVnetLinks
  ]
}

module containerAppsEnvironment 'br/public:avm/res/app/managed-environment:0.15.0' = {
  name: 'container-apps-environment'
  params: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsWorkspaceResourceId: logAnalytics.outputs.resourceId
    }
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalytics.outputs.resourceId
      }
    ]
    dockerBridgeCidr: '172.16.0.1/28'
    infrastructureResourceGroupName: names.containerAppsInfrastructureResourceGroup
    infrastructureSubnetResourceId: containerAppsSubnetResourceId
    internal: false
    location: location
    name: names.containerAppsEnvironment
    peerTrafficEncryption: true
    platformReservedCidr: '172.17.0.0/16'
    platformReservedDnsIP: '172.17.0.10'
    publicNetworkAccess: 'Enabled'
    tags: commonTags
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: enableZoneRedundancy
  }
  dependsOn: [
    network
  ]
}

module staticWebApp 'br/public:avm/res/web/static-site:0.9.5' = {
  name: 'static-web-app'
  params: {
    allowConfigFileUpdates: true
    enterpriseGradeCdnStatus: 'Disabled'
    location: staticWebAppLocation
    name: names.staticWebApp
    publicNetworkAccess: 'Enabled'
    sku: effectiveStaticWebAppSku
    stagingEnvironmentPolicy: 'Disabled'
    tags: union(commonTags, {
      'azd-service-name': 'dashboard'
    })
  }
}

var sqlConnectionString = 'Server=tcp:${sql.outputs.fullyQualifiedDomainName},1433;Initial Catalog=${names.sqlDatabase};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;User Id=${runtimeIdentity.outputs.clientId}'
var migrationSqlConnectionString = 'Server=tcp:${sql.outputs.fullyQualifiedDomainName},1433;Initial Catalog=${names.sqlDatabase};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;User Id=${migrationIdentity.outputs.clientId}'
var githubPrivateKeySecretUrl = '${keyVault.outputs.uri}secrets/github-app-private-key'
var reportBlobContainerUri = '${storage.outputs.primaryBlobEndpoint}raw-reports'
var serviceBusFullyQualifiedNamespace = '${names.serviceBusNamespace}.servicebus.windows.net'
var apiImage = '${containerRegistry.outputs.loginServer}/budget-manager-api:${apiImageTag}'
var workerImage = '${containerRegistry.outputs.loginServer}/budget-manager-worker:${workerImageTag}'

module api 'br/public:avm/res/app/container-app:0.23.0' = if (deployApplicationResources) {
  name: 'api'
  params: {
    activeRevisionsMode: 'Single'
    containers: [
      {
        env: [
          {
            name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
            value: 'true'
          }
          {
            name: 'Authentication__Audience'
            value: apiAudience
          }
          {
            name: 'Authentication__Authority'
            value: '${environment().authentication.loginEndpoint}${tenantId}/v2.0'
          }
          {
            name: 'Authentication__Enabled'
            value: 'true'
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: runtimeIdentity.outputs.clientId
          }
          {
            name: 'BudgetGuardrails__CooldownHours'
            value: string(budgetCooldownHours)
          }
          {
            name: 'BudgetGuardrails__ApprovalLifetimeHours'
            value: string(budgetApprovalLifetimeHours)
          }
          {
            name: 'BudgetGuardrails__ForecastHeadroomPercent'
            value: string(budgetForecastHeadroomPercent)
          }
          {
            name: 'BudgetGuardrails__MaximumCumulativeMonthlyIncrease'
            value: string(budgetMaximumCumulativeMonthlyIncrease)
          }
          {
            name: 'BudgetGuardrails__MaximumDataAgeHours'
            value: string(budgetMaximumDataAgeHours)
          }
          {
            name: 'BudgetGuardrails__MaximumIncreaseAmount'
            value: string(budgetMaximumIncreaseAmount)
          }
          {
            name: 'BudgetGuardrails__MaximumIncreasePercent'
            value: string(budgetMaximumIncreasePercent)
          }
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: applicationInsights.outputs.connectionString
          }
          {
            name: 'ConnectionStrings__BudgetManager'
            value: sqlConnectionString
          }
          {
            name: 'Database__Initialize'
            value: 'false'
          }
          {
            name: 'Database__Provider'
            value: 'SqlServer'
          }
          {
            name: 'WorkflowEvents__AdminPrincipalNames'
            value: notificationRecipients
          }
          {
            name: 'WorkflowEvents__DashboardUrl'
            value: 'https://${staticWebApp.outputs.defaultHostname}'
          }
          {
            name: 'PUBLISH_BUDGET_LIFECYCLE_EVENTS'
            value: string(publishBudgetLifecycleEvents)
          }
        ]
        image: apiImage
        name: 'api'
        probes: [
          {
            httpGet: {
              path: '/healthz'
              port: 8080
              scheme: 'HTTP'
            }
            initialDelaySeconds: 10
            periodSeconds: 30
            type: 'Liveness'
          }
          {
            httpGet: {
              path: '/readyz'
              port: 8080
              scheme: 'HTTP'
            }
            initialDelaySeconds: 5
            periodSeconds: 10
            type: 'Readiness'
          }
        ]
        resources: {
          cpu: json(apiCpu)
          memory: apiMemory
        }
      }
    ]
    environmentResourceId: containerAppsEnvironment.outputs.resourceId
    ingressAllowInsecure: false
    ingressExternal: true
    ingressTargetPort: 8080
    ingressTransport: 'auto'
    location: location
    managedIdentities: {
      userAssignedResourceIds: [
        runtimeIdentity.outputs.resourceId
      ]
    }
    name: names.api
    registries: [
      {
        identity: runtimeIdentity.outputs.resourceId
        server: containerRegistry.outputs.loginServer
      }
    ]
    scaleSettings: {
      maxReplicas: effectiveApiMaxReplicas
      minReplicas: apiMinReplicas
    }
    tags: union(commonTags, {
      'azd-service-name': 'api'
    })
  }
  dependsOn: [
    keyVault
    serviceBus
    storage
  ]
}

var baseWorkerEnvironment = [
  {
    name: 'AZURE_CLIENT_ID'
    value: runtimeIdentity.outputs.clientId
  }
  {
    name: 'BUDGET_MANAGER_SQL_CONNECTION_STRING'
    value: sqlConnectionString
  }
  {
    name: 'BUDGET_WRITES_ENABLED'
    value: string(budgetWritesEnabled)
  }
  {
    name: 'DASHBOARD_URL'
    value: 'https://${staticWebApp.outputs.defaultHostname}'
  }
  {
    name: 'NOTIFICATION_ADMIN_RECIPIENTS'
    value: notificationRecipients
  }
  {
    name: 'NOTIFICATION_CHANNELS'
    value: notificationChannels
  }
  {
    name: 'PUBLISH_BUDGET_LIFECYCLE_EVENTS'
    value: string(publishBudgetLifecycleEvents)
  }
]
var enterpriseWorkerEnvironment = [
  {
    name: 'GITHUB_ENTERPRISE_ID'
    value: enterpriseId
  }
]
var githubWorkerEnvironment = concat(enterpriseWorkerEnvironment, [
  {
    name: 'GITHUB_APP_INSTALLATION_ID'
    value: string(githubAppInstallationId)
  }
  {
    name: 'GITHUB_APP_ISSUER'
    value: githubAppIssuer
  }
  {
    name: 'GITHUB_ENTERPRISE_SLUG'
    value: enterpriseSlug
  }
])
var workerJobDefinitions = [
  {
    command: 'migrate'
    cronExpression: ''
    environmentKind: 'migration'
    includeGitHubPrivateKey: false
    triggerType: 'Manual'
  }
  {
    command: 'sync-budgets'
    cronExpression: syncBudgetsSchedule
    environmentKind: 'github'
    includeGitHubPrivateKey: true
    triggerType: 'Schedule'
  }
  {
    command: 'ingest-daily'
    cronExpression: ingestDailySchedule
    environmentKind: 'reports'
    includeGitHubPrivateKey: true
    triggerType: 'Schedule'
  }
  {
    command: 'sync-identities'
    cronExpression: syncIdentitiesSchedule
    environmentKind: 'graph'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
  {
    command: 'classify'
    cronExpression: classifySchedule
    environmentKind: 'enterprise'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
  {
    command: 'plan-notifications'
    cronExpression: planNotificationsSchedule
    environmentKind: 'enterprise'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
  {
    command: 'forecast'
    cronExpression: forecastSchedule
    environmentKind: 'enterprise'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
  {
    command: 'reconcile-baselines'
    cronExpression: reconcileBaselinesSchedule
    environmentKind: 'enterprise'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
  {
    command: 'apply-retention'
    cronExpression: applyRetentionSchedule
    environmentKind: 'enterprise'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
  {
    command: 'execute-approved'
    cronExpression: executeApprovedSchedule
    environmentKind: 'github'
    includeGitHubPrivateKey: true
    triggerType: budgetWritesEnabled ? 'Schedule' : 'Manual'
  }
  {
    command: 'dispatch-outbox'
    cronExpression: dispatchOutboxSchedule
    environmentKind: 'messaging'
    includeGitHubPrivateKey: false
    triggerType: 'Schedule'
  }
]
var workerEnvironments = {
  base: baseWorkerEnvironment
  enterprise: concat(baseWorkerEnvironment, enterpriseWorkerEnvironment)
  github: concat(baseWorkerEnvironment, githubWorkerEnvironment)
  graph: concat(baseWorkerEnvironment, enterpriseWorkerEnvironment, [
    {
      name: 'GRAPH_GITHUB_LOGIN_PROPERTY'
      value: graphGitHubLoginProperty
    }
  ])
  messaging: concat(baseWorkerEnvironment, [
    {
      name: 'SERVICE_BUS_NAMESPACE'
      value: serviceBusFullyQualifiedNamespace
    }
    {
      name: 'SERVICE_BUS_QUEUE_NAME'
      value: serviceBusQueueName
    }
  ])
  migration: [
    {
      name: 'AZURE_CLIENT_ID'
      value: migrationIdentity.outputs.clientId
    }
    {
      name: 'BUDGET_MANAGER_SQL_CONNECTION_STRING'
      value: migrationSqlConnectionString
    }
  ]
  reports: concat(baseWorkerEnvironment, githubWorkerEnvironment, [
    {
      name: 'REPORT_BLOB_CONTAINER_URI'
      value: reportBlobContainerUri
    }
    {
      name: 'SIGNED_REPORT_ALLOWED_HOSTS'
      value: signedReportAllowedHosts
    }
  ])
}

module workerJobs 'modules/worker-job.bicep' = [for jobConfig in workerJobDefinitions: if (deployApplicationResources) {
  name: 'worker-${jobConfig.command}'
  params: {
    command: jobConfig.command
    cpu: workerCpu
    cronExpression: jobConfig.cronExpression
    environmentResourceId: containerAppsEnvironment.outputs.resourceId
    environmentVariables: workerEnvironments[jobConfig.environmentKind]
    githubPrivateKeySecretUrl: githubPrivateKeySecretUrl
    identityResourceId: jobConfig.command == 'migrate'
      ? migrationIdentity.outputs.resourceId
      : runtimeIdentity.outputs.resourceId
    image: workerImage
    includeGitHubPrivateKey: jobConfig.includeGitHubPrivateKey
    location: location
    memory: workerMemory
    name: 'job-${take(replace(jobConfig.command, '-', ''), 14)}-${take(compactEnvironmentName, 6)}-${take(deploymentToken, 4)}'
    registryServer: containerRegistry.outputs.loginServer
    replicaTimeout: workerReplicaTimeoutSeconds
    tags: commonTags
    triggerType: jobConfig.triggerType
  }
  dependsOn: [
    serviceBus
  ]
}]

module notifications 'modules/notifications.bicep' = {
  name: 'notifications'
  params: {
    identityResourceId: notificationIdentity.outputs.resourceId
    location: location
    logAnalyticsWorkspaceResourceId: logAnalytics.outputs.resourceId
    name: names.logicApp
    notificationRecipients: notificationRecipients
    serviceBusNamespaceName: names.serviceBusNamespace
    serviceBusQueueName: serviceBusQueueName
    tags: commonTags
    teamsChannelId: teamsChannelId
    teamsTeamId: teamsTeamId
  }
  dependsOn: [
    serviceBus
  ]
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.outputs.name
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.name
output AZURE_RESOURCE_GROUP_NAME string = resourceGroup().name
output AZURE_STATIC_WEB_APP_NAME string = staticWebApp.outputs.name
output AZURE_STATIC_WEB_APP_URL string = 'https://${staticWebApp.outputs.defaultHostname}'
output MIGRATION_JOB_NAME string = 'job-${take(replace('migrate', '-', ''), 14)}-${take(compactEnvironmentName, 6)}-${take(deploymentToken, 4)}'
output MIGRATION_IDENTITY_CLIENT_ID string = migrationIdentity.outputs.clientId
output MIGRATION_IDENTITY_NAME string = migrationIdentity.outputs.name
output MIGRATION_IDENTITY_PRINCIPAL_ID string = migrationIdentity.outputs.principalId
output NOTIFICATION_WORKFLOW_NAME string = notifications.outputs.workflowName
output RUNTIME_IDENTITY_CLIENT_ID string = runtimeIdentity.outputs.clientId
output RUNTIME_IDENTITY_NAME string = runtimeIdentity.outputs.name
output RUNTIME_IDENTITY_PRINCIPAL_ID string = runtimeIdentity.outputs.principalId
output SERVICE_API_RESOURCE_ID string = api.?outputs.?resourceId ?? ''
output SERVICE_API_URI string = empty(api.?outputs.?fqdn) ? '' : 'https://${api.?outputs.?fqdn}'
output SERVICE_BUS_NAMESPACE string = serviceBusFullyQualifiedNamespace
output SERVICE_BUS_QUEUE_NAME string = serviceBusQueueName
output SQL_DATABASE_NAME string = names.sqlDatabase
output SQL_SERVER_FQDN string = sql.outputs.fullyQualifiedDomainName
output DASHBOARD_CLIENT_ID string = dashboardClientId
output DASHBOARD_API_SCOPE string = dashboardApiScope
output ENTRA_TENANT_ID string = tenantId
output deploymentLocation string = location
output resourceNames object = names