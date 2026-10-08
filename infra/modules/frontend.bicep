@description('Azure region for the web application and hosting plan.')
param location string

@description('App Service name for the Blazor Server BFF.')
param webAppName string

@description('Linux App Service plan name.')
param webPlanName string

@description('Deployed Functions API base address, including the /api/ prefix.')
param functionEndpoint string

@description('Key Vault URI used by App Service references.')
param keyVaultUri string

@description('Microsoft identity platform login endpoint for the current Azure cloud.')
param loginEndpoint string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Common resource tags.')
param tags object

var webClientSecretReference = '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/web-client-secret)'
var functionKeyReference = '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/function-api-key)'

resource webPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: webPlanName
  location: location
  kind: 'linux'
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: webPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'AzureAd__Instance'
          value: loginEndpoint
        }
        {
          name: 'AzureAd__TenantId'
          value: 'consumers'
        }
        {
          name: 'AzureAd__CallbackPath'
          value: '/signin-oidc'
        }
        {
          name: 'AzureAd__ClientSecret'
          value: webClientSecretReference
        }
        {
          name: 'TalksOpsApi__BaseAddress'
          value: functionEndpoint
        }
        {
          name: 'TalksOpsApi__FunctionKey'
          value: functionKeyReference
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
      ]
    }
  }
}

output webAppName string = webApp.name
output webEndpoint string = 'https://${webApp.properties.defaultHostName}'
output webPrincipalId string = webApp.identity.principalId
