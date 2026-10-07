@description('Azure region for monitoring resources.')
param location string

@description('Application Insights component name.')
param appInsightsName string

@description('Log Analytics workspace name.')
param workspaceName string

@description('Common resource tags.')
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    IngestionMode: 'LogAnalytics'
    WorkspaceResourceId: workspace.id
  }
}

output appInsightsConnectionString string = appInsights.properties.ConnectionString
