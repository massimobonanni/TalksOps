@description('Storage account that contains the TalksOps application tables.')
param dataStorageAccountName string

@description('Name of the TalksOps event and proposal table.')
param eventTableName string

@description('Name of the TalksOps token-consumption table.')
param consumptionTableName string

@description('Key Vault name containing server-side application credentials.')
param keyVaultName string

@description('System-assigned managed identity principal ID for the Function App.')
param functionPrincipalId string

@description('System-assigned managed identity principal ID for the Web App.')
param webPrincipalId string

@description('Microsoft Foundry account name.')
param foundryAccountName string

@description('Microsoft Foundry project name.')
param foundryProjectName string

var tableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var foundryUserRoleId = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

resource dataStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: dataStorageAccountName
}

resource dataTableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' existing = {
  parent: dataStorageAccount
  name: 'default'
}

resource eventTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' existing = {
  parent: dataTableService
  name: eventTableName
}

resource consumptionTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' existing = {
  parent: dataTableService
  name: consumptionTableName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource functionAppEventTableAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventTable.id, functionPrincipalId, tableDataContributorRoleId)
  scope: eventTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource functionAppConsumptionTableAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(consumptionTable.id, functionPrincipalId, tableDataContributorRoleId)
  scope: consumptionTable
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

resource functionFoundryAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryProject.id, functionPrincipalId, foundryUserRoleId)
  scope: foundryProject
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', foundryUserRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}
