targetScope = 'resourceGroup'

@description('Container Apps Job resource name.')
param name string

@description('Azure region for the job.')
param location string

@description('Resource ID of the Container Apps managed environment.')
param environmentResourceId string

@description('Resource ID of the user-assigned managed identity.')
param identityResourceId string

@description('ACR login server hosting the worker image.')
param registryServer string

@description('Fully qualified worker container image reference.')
param image string

@description('Worker command passed to the image entry point.')
param command string

@allowed([
  'Manual'
  'Schedule'
])
@description('Job trigger type.')
param triggerType string

@description('UTC cron expression. Required for Schedule jobs and ignored for Manual jobs.')
param cronExpression string = ''

@description('Environment variables consumed by this worker command.')
param environmentVariables array

@description('Expose the GitHub App private key through a Key Vault-backed Container Apps secret.')
param includeGitHubPrivateKey bool = false

@description('Versionless Key Vault URL for the GitHub App private key secret.')
param githubPrivateKeySecretUrl string = ''

@description('Container CPU cores.')
param cpu string = '0.5'

@description('Container memory.')
param memory string = '1Gi'

@description('Maximum execution duration in seconds.')
param replicaTimeout int = 3600

@description('Maximum replica retry count.')
param replicaRetryLimit int = 2

@description('Tags applied to the job.')
param tags object = {}

var githubPrivateKeyEnvironment = includeGitHubPrivateKey
  ? [
      {
        name: 'GITHUB_APP_PRIVATE_KEY'
        secretRef: 'github-app-private-key'
      }
    ]
  : []

var jobContainers = [
  {
    args: [
      command
    ]
    env: concat(environmentVariables, githubPrivateKeyEnvironment)
    image: image
    name: 'worker'
    resources: {
      cpu: cpu
      memory: memory
    }
  }
]
var jobManagedIdentities = {
  userAssignedResourceIds: [
    identityResourceId
  ]
}
var jobRegistries = [
  {
    identity: identityResourceId
    server: registryServer
  }
]
var jobSecrets = includeGitHubPrivateKey
  ? [
      {
        identity: identityResourceId
        keyVaultUrl: githubPrivateKeySecretUrl
        name: 'github-app-private-key'
      }
    ]
  : []

module manualJob 'br/public:avm/res/app/job:0.7.2' = if (triggerType == 'Manual') {
  name: 'job-manual-${uniqueString(name)}'
  params: {
    containers: jobContainers
    environmentResourceId: environmentResourceId
    location: location
    managedIdentities: jobManagedIdentities
    manualTriggerConfig: {
      parallelism: 1
      replicaCompletionCount: 1
    }
    name: name
    registries: jobRegistries
    replicaRetryLimit: replicaRetryLimit
    replicaTimeout: replicaTimeout
    secrets: jobSecrets
    tags: tags
    triggerType: 'Manual'
  }
}
module scheduledJob 'br/public:avm/res/app/job:0.7.2' = if (triggerType == 'Schedule') {
  name: 'job-scheduled-${uniqueString(name)}'
  params: {
    containers: jobContainers
    environmentResourceId: environmentResourceId
    location: location
    managedIdentities: jobManagedIdentities
    name: name
    registries: jobRegistries
    replicaRetryLimit: replicaRetryLimit
    replicaTimeout: replicaTimeout
    scheduleTriggerConfig: {
      cronExpression: cronExpression
      parallelism: 1
      replicaCompletionCount: 1
    }
    secrets: jobSecrets
    tags: tags
    triggerType: 'Schedule'
  }
}