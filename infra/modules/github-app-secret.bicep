targetScope = 'resourceGroup'

@description('Existing Key Vault name.')
param keyVaultName string

@secure()
@description('GitHub App PEM private key.')
param githubAppPrivateKey string

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource githubAppPrivateKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: keyVault
  name: 'github-app-private-key'
  properties: {
    attributes: {
      enabled: true
    }
    contentType: 'application/x-pem-file'
    value: githubAppPrivateKey
  }
}

output secretId string = githubAppPrivateKeySecret.id