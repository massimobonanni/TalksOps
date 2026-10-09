targetScope = 'subscription'

@description('Short project identifier used in Azure resource names and tags.')
@minLength(2)
@maxLength(12)
param environmentName string = 'talksops'

@description('Additional resource tags, including AZD environment discovery metadata.')
param resourceTags object = {}

@description('Azure region selected for this AZD environment.')
param location string

@description('Object ID of the identity running AZD. It receives Key Vault Secrets Officer for deployment-time secret creation and the post-provision identity hook.')
param deploymentPrincipalId string

@description('Principal type of the identity running AZD.')
@allowed([
  'User'
  'ServicePrincipal'
])
param deploymentPrincipalType string = 'User'

var abbreviations = loadJsonContent('./abbreviations.json')
var resourceGroupName = '${abbreviations.rg}-${environmentName}'
var suffix = uniqueString(resourceGroup.id)
var compactProject = toLower(replace(environmentName, '-', ''))
var dataStorageAccountName = take('${abbreviations.st}${suffix}${compactProject}', 24)
var functionStorageAccountName = take('${abbreviations.st}func${suffix}${compactProject}', 24)
var keyVaultName = take('${abbreviations.kv}-${compactProject}-${suffix}', 24)
var webAppName = take('${abbreviations.app}-${environmentName}-web-${suffix}', 60)
var functionAppName = take('${abbreviations.app}-${environmentName}-api-${suffix}', 60)
var appInsightsName = take('${abbreviations.appi}-${environmentName}-${suffix}', 60)
var workspaceName = take('${abbreviations.log}-${environmentName}-${suffix}', 63)
var functionPlanName = take('${abbreviations.asp}-${environmentName}-api-${suffix}', 40)
var webPlanName = take('${abbreviations.asp}-${environmentName}-web-${suffix}', 40)
var appRegistrationDisplayName = 'TalksOps-${environmentName}-${suffix}'
var tags = union({
  project: environmentName
  managedBy: 'azd'
}, resourceTags)

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring-${suffix}'
  scope: resourceGroup
  params: {
    location: location
    appInsightsName: appInsightsName
    workspaceName: workspaceName
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage-${suffix}'
  scope: resourceGroup
  params: {
    location: location
    storageAccountName: dataStorageAccountName
    tableName: 'TalksOps'
    tags: tags
  }
}

module backend 'modules/backend.bicep' = {
  name: 'backend-${suffix}'
  scope: resourceGroup
  params: {
    location: location
    functionAppName: functionAppName
    functionPlanName: functionPlanName
    functionStorageAccountName: functionStorageAccountName
    dataTableServiceUri: storage.outputs.tableServiceUri
    dataTableName: storage.outputs.tableName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-${suffix}'
  scope: resourceGroup
  params: {
    location: location
    keyVaultName: keyVaultName
    functionAppName: backend.outputs.functionAppName
    deploymentPrincipalId: deploymentPrincipalId
    deploymentPrincipalType: deploymentPrincipalType
    tags: tags
  }
}

module frontend 'modules/frontend.bicep' = {
  name: 'frontend-${suffix}'
  scope: resourceGroup
  params: {
    location: location
    webAppName: webAppName
    webPlanName: webPlanName
    functionEndpoint: backend.outputs.functionEndpoint
    keyVaultUri: keyVault.outputs.keyVaultUri
    loginEndpoint: environment().authentication.loginEndpoint
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    tags: tags
  }
}

module identity 'modules/identity.bicep' = {
  name: 'identity-${suffix}'
  scope: resourceGroup
  params: {
    dataStorageAccountName: storage.outputs.storageAccountName
    dataTableName: storage.outputs.tableName
    keyVaultName: keyVault.outputs.keyVaultName
    functionPrincipalId: backend.outputs.functionPrincipalId
    webPrincipalId: frontend.outputs.webPrincipalId
  }
}

output AZURE_RESOURCE_GROUP string = resourceGroup.name
output API_ENDPOINT_URL string = backend.outputs.functionEndpoint
output WEB_ENDPOINT_URL string = frontend.outputs.webEndpoint
output AZURE_FUNCTION_APP_NAME string = backend.outputs.functionAppName
output AZURE_WEB_APP_NAME string = frontend.outputs.webAppName
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.keyVaultName
output AZURE_AD_APP_DISPLAY_NAME string = appRegistrationDisplayName
