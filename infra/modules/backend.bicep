@description('Azure region for the Function App and hosting plan.')
param location string

@description('Function App name.')
param functionAppName string

@description('Flex Consumption plan name.')
param functionPlanName string

@description('Storage account used by the Functions host and deployment package.')
param storageAccountName string

@description('Azure Table service endpoint used by application repositories.')
param tableServiceUri string

@description('Private blob container URI for Flex Consumption package deployment.')
param packageContainerUri string

@description('Azure Table name for application data.')
param tableName string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Common resource tags.')
param tags object

resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: functionPlanName
  location: location
  kind: 'functionapp,linux'
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: packageContainerUri
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'Storage__TableServiceUri'
          value: tableServiceUri
        }
        {
          name: 'StorageTableName'
          value: tableName
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output functionEndpoint string = 'https://${functionApp.properties.defaultHostName}/api/'
output functionPrincipalId string = functionApp.identity.principalId
