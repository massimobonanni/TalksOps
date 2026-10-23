@description('Azure region for the Function App and hosting plan.')
param location string

@description('Function App name.')
param functionAppName string

@description('Flex Consumption plan name.')
param functionPlanName string

@description('Storage account dedicated to Functions host state and Flex Consumption deployment packages.')
param functionStorageAccountName string

@description('Azure Table service endpoint for TalksOps application data.')
param dataTableServiceUri string

@description('Azure Table name used by TalksOps event and proposal data.')
param eventTableName string

@description('Azure Table name used by TalksOps token-consumption data.')
param consumptionTableName string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Microsoft Foundry account endpoint used by event extraction.')
param foundryEndpoint string

@description('Microsoft Foundry project name used by event extraction.')
param foundryProjectName string

@description('Microsoft Foundry model deployment name used by event extraction.')
param foundryDeploymentName string

@description('Number of event extraction requests allowed per user in each rate-limit window.')
@minValue(1)
param eventExtractionRateLimitPermitLimit int

@description('Event extraction rate-limit window length in seconds.')
@minValue(1)
param eventExtractionRateLimitWindowSeconds int

@description('Common resource tags.')
param tags object

var packageContainerName = 'function-packages'
var blobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource functionStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: functionStorageAccountName
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

resource functionBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: functionStorageAccount
  name: 'default'
}

resource packageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: functionBlobService
  name: packageContainerName
  properties: {
    publicAccess: 'None'
  }
}

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
  tags: union(tags, {
    'azd-service-name': 'api'
  })
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
          value: '${functionStorageAccount.properties.primaryEndpoints.blob}${packageContainer.name}'
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
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: functionStorageAccount.name
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'StorageUri'
          value: dataTableServiceUri
        }
        {
          name: 'StorageTableName'
          value: eventTableName
        }
        {
          name: 'StorageTokensConsumptionTableName'
          value: consumptionTableName
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'FoundryEndpoint'
          value: foundryEndpoint
        }
        {
          name: 'FoundryProjectName'
          value: foundryProjectName
        }
        {
          name: 'FoundryDeploymentName'
          value: foundryDeploymentName
        }
        {
          name: 'EventDataExtraction__RateLimit__PermitLimit'
          value: string(eventExtractionRateLimitPermitLimit)
        }
        {
          name: 'EventDataExtraction__RateLimit__WindowSeconds'
          value: string(eventExtractionRateLimitWindowSeconds)
        }
      ]
    }
  }
}

resource functionHostStorageBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionStorageAccount.id, functionApp.id, blobDataOwnerRoleId)
  scope: functionStorageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataOwnerRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output functionEndpoint string = 'https://${functionApp.properties.defaultHostName}/'
output functionPrincipalId string = functionApp.identity.principalId
output functionStorageAccountName string = functionStorageAccount.name
