@description('Storage account that contains the TalksOps application table.')
param dataStorageAccountName string

@description('Name of the TalksOps application table.')
param dataTableName string

@description('Storage account dedicated to Functions host and deployment data.')
param functionStorageAccountName string

@description('Key Vault name containing server-side application credentials.')
param keyVaultName string

@description('System-assigned managed identity principal ID for the Function App.')
param functionPrincipalId string

@description('System-assigned managed identity principal ID for the Web App.')
param webPrincipalId string

var blobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var tableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource dataStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: dataStorageAccountName
}

resource dataTableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' existing = {
  parent: dataStorageAccount
  name: 'default'
}

resource dataTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' existing = {
  parent: dataTableService
  name: dataTableName
}

resource functionStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: functionStorageAccountName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource functionHostStorageBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionStorageAccount.id, functionPrincipalId, blobDataOwnerRoleId)
  scope: functionStorageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataOwnerRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource functionAppTableAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataTable.id, functionPrincipalId, tableDataContributorRoleId)
  scope: dataTable
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
