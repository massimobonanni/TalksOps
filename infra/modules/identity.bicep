@description('Storage account that hosts the Functions runtime and the TalksOps table.')
param storageAccountName string

@description('Table name containing application data.')
param tableName string

@description('Key Vault name containing server-side application credentials.')
param keyVaultName string

@description('System-assigned managed identity principal ID for the Function App.')
param functionPrincipalId string

@description('System-assigned managed identity principal ID for the Web App.')
param webPrincipalId string

@description('Object ID of the AZD deployment identity.')
param deploymentPrincipalId string

@description('Principal type of the AZD deployment identity.')
@allowed([
  'User'
  'ServicePrincipal'
])
param deploymentPrincipalType string

var blobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var queueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var tableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var keyVaultSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource appTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' existing = {
  parent: tableService
  name: tableName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource functionBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionPrincipalId, blobDataOwnerRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataOwnerRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource functionQueueAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionPrincipalId, queueDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', queueDataContributorRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource functionTableAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appTable.id, functionPrincipalId, tableDataContributorRoleId)
  scope: appTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource webVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, webPrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
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
