@description('Azure region for the data and deployment storage account.')
param location string

@description('Globally unique storage account name, 3-24 lowercase letters and numbers.')
param storageAccountName string

@description('Table used by TalksOps application data.')
param tableName string

@description('Private blob container used for Flex Consumption deployment packages.')
param packageContainerName string

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

resource appTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: tableName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource packageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: packageContainerName
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountName string = storageAccount.name
output tableName string = appTable.name
output tableServiceUri string = 'https://${storageAccount.name}.table.${environment().suffixes.storage}/'
output packageContainerName string = packageContainer.name
output packageContainerUri string = '${storageAccount.properties.primaryEndpoints.blob}${packageContainer.name}'
