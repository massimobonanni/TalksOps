@description('Storage account that contains the TalksOps application table.')
param dataStorageAccountName string

@description('Name of the TalksOps application table.')
param dataTableName string

@description('Key Vault name containing server-side application credentials.')
param keyVaultName string

@description('System-assigned managed identity principal ID for the Function App.')
param functionPrincipalId string

@description('System-assigned managed identity principal ID for the Web App.')
param webPrincipalId string

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

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
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
