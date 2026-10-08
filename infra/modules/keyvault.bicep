@description('Azure region for Key Vault.')
param location string

@description('Key Vault name.')
param keyVaultName string

@description('Function App whose generated default host key is stored in this vault.')
param functionAppName string

@description('Object ID of the identity deploying this template.')
param deploymentPrincipalId string

@description('Principal type of the identity deploying this template.')
@allowed([
  'User'
  'ServicePrincipal'
])
param deploymentPrincipalType string

@description('Common resource tags.')
param tags object

var keyVaultSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

resource deploymentVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, deploymentPrincipalId, keyVaultSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficerRoleId)
    principalId: deploymentPrincipalId
    principalType: deploymentPrincipalType
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

resource functionKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'function-api-key'
  properties: {
    contentType: 'Azure Functions host key'
    value: listKeys('${functionApp.id}/host/default', functionApp.apiVersion).functionKeys.default
  }
  dependsOn: [
    deploymentVaultAccess
  ]
}

output keyVaultName string = keyVault.name
output keyVaultUri string = 'https://${keyVault.name}.${environment().suffixes.keyvaultDns}/'
