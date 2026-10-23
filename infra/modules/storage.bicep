@description('Azure region for the TalksOps data storage account.')
param location string

@description('Globally unique data storage account name, 3-24 lowercase letters and numbers.')
param storageAccountName string

@description('Table used by TalksOps event and proposal data.')
param eventTableName string

@description('Table used by TalksOps token-consumption data.')
param consumptionTableName string

@description('Common resource tags.')
param tags object

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource eventTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: eventTableName
}

resource consumptionTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: consumptionTableName
}

output storageAccountName string = storageAccount.name
output eventTableName string = eventTable.name
output consumptionTableName string = consumptionTable.name
output tableServiceUri string = 'https://${storageAccount.name}.table.${environment().suffixes.storage}/'
