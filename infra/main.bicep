targetScope = 'resourceGroup'

@description('Short project identifier used in Azure resource names and tags.')
@minLength(2)
@maxLength(12)
param projectName string = 'talksops'

@description('AZD environment name. Keep it short because it contributes to resource names.')
@minLength(1)
@maxLength(12)
param environmentName string

@description('Azure region selected for this AZD environment.')
param location string = resourceGroup().location

@description('Object ID of the identity running AZD. It receives Key Vault Secrets Officer for deployment-time secret creation and the post-provision identity hook.')
param deploymentPrincipalId string

@description('Principal type of the identity running AZD.')
@allowed([
  'User'
  'ServicePrincipal'
])
param deploymentPrincipalType string = 'User'

var suffix = uniqueString(resourceGroup().id, environmentName)
var compactEnvironment = toLower(replace(environmentName, '-', ''))
var dataStorageAccountName = take('st${suffix}${compactEnvironment}', 24)
var functionStorageAccountName = take('func${suffix}${compactEnvironment}', 24)
var keyVaultName = take('kv-${compactEnvironment}-${suffix}', 24)
var webAppName = take('${projectName}-web-${environmentName}-${suffix}', 60)
var functionAppName = take('${projectName}-api-${environmentName}-${suffix}', 60)
var appInsightsName = take('appi-${projectName}-${environmentName}-${suffix}', 60)
var workspaceName = take('log-${projectName}-${environmentName}-${suffix}', 63)
var functionPlanName = take('asp-${projectName}-api-${environmentName}-${suffix}', 40)
var webPlanName = take('asp-${projectName}-web-${environmentName}-${suffix}', 40)
var appRegistrationDisplayName = 'TalksOps-${environmentName}-${suffix}'
var tags = {
  project: projectName
  environment: environmentName
  managedBy: 'azd'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring-${suffix}'
  params: {
    location: location
    appInsightsName: appInsightsName
    workspaceName: workspaceName
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage-${suffix}'
  params: {
    location: location
    storageAccountName: dataStorageAccountName
    tableName: 'TalksOps'
    tags: tags
  }
}

module backend 'modules/backend.bicep' = {
  name: 'backend-${suffix}'
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
  params: {
    dataStorageAccountName: storage.outputs.storageAccountName
    dataTableName: storage.outputs.tableName
    functionStorageAccountName: backend.outputs.functionStorageAccountName
    keyVaultName: keyVault.outputs.keyVaultName
    functionPrincipalId: backend.outputs.functionPrincipalId
    webPrincipalId: frontend.outputs.webPrincipalId
  }
}

output API_ENDPOINT_URL string = backend.outputs.functionEndpoint
output WEB_ENDPOINT_URL string = frontend.outputs.webEndpoint
output AZURE_FUNCTION_APP_NAME string = backend.outputs.functionAppName
output AZURE_WEB_APP_NAME string = frontend.outputs.webAppName
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.keyVaultName
output AZURE_AD_APP_DISPLAY_NAME string = appRegistrationDisplayName
