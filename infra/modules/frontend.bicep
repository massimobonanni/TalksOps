@description('Azure region for the web application and hosting plan.')
param location string

@description('App Service name for the Blazor Server BFF.')
param webAppName string

@description('Linux App Service plan name.')
param webPlanName string

@description('App Service plan SKU, for example F1, B1, or S1.')
param webPlanSku string

@description('Deployed Function App base address, without the /api/ route prefix.')
param functionEndpoint string

@description('Key Vault URI used by App Service references.')
param keyVaultUri string

@description('Microsoft identity platform login endpoint for the current Azure cloud.')
param loginEndpoint string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Whether the AI event extraction feature is available in the web UI.')
param aiExtractionEnabled bool = false

@description('Common resource tags.')
param tags object

var webClientSecretReference = '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/web-client-secret)'
var functionKeyReference = '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/function-api-key)'
var webPlanSkuUpper = toUpper(webPlanSku)
var webPlanTier = startsWith(webPlanSkuUpper, 'F') ? 'Free'
  : startsWith(webPlanSkuUpper, 'D') ? 'Shared'
  : startsWith(webPlanSkuUpper, 'B') ? 'Basic'
  : startsWith(webPlanSkuUpper, 'S') ? 'Standard'
  : startsWith(webPlanSkuUpper, 'P') ? (contains(webPlanSkuUpper, 'V4') ? 'PremiumV4' : contains(webPlanSkuUpper, 'V3') ? 'PremiumV3' : contains(webPlanSkuUpper, 'V2') ? 'PremiumV2' : 'Premium')
  : startsWith(webPlanSkuUpper, 'I') ? 'IsolatedV2'
  : ''

resource webPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: webPlanName
  location: location
  kind: 'linux'
  tags: tags
  sku: {
    name: webPlanSku
    tier: webPlanTier
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
  tags: union(tags, {
    'azd-service-name': 'web'
  })
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: webPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: webPlanTier != 'Free' && webPlanTier != 'Shared'
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
        {
          name: 'Features__AiExtraction'
          value: aiExtractionEnabled ? 'true' : 'false'
        }
      ]
    }
  }
}

output webAppName string = webApp.name
output webEndpoint string = 'https://${webApp.properties.defaultHostName}'
output webPrincipalId string = webApp.identity.principalId
