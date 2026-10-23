@description('Azure region for monitoring resources.')
param location string

@description('Application Insights component name.')
param appInsightsName string

@description('Log Analytics workspace name.')
param workspaceName string

@description('Common resource tags.')
param tags object

@description('Query pack name.')
param querypackName string 

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

resource querypack 'Microsoft.OperationalInsights/querypacks@2026-03-01' = {
  name: querypackName
  location: location
  properties: {}
}

resource query_EventDataExtractionServiceTraces 'Microsoft.OperationalInsights/querypacks/queries@2026-03-01' = {
  parent: querypack
  name: '54c37d7e-cd84-4627-9402-cfa7a31b929d'
  properties: {
    displayName: 'EventDataExtractionService traces'
    description: 'Traces of the EventDataExtractionService service in Azure Functions'
    body: 'traces\r\n| where timestamp > ago(30m)\r\n| where tostring(customDimensions.CategoryName)\r\n    == "TalksOps.AI.Services.EventDataExtractionService"\r\n| project timestamp, severityLevel, message, customDimensions\r\n| order by timestamp desc'
    related: {
      categories: [
        'applications'
      ]
      resourceTypes: [
        'microsoft.insights/components'
      ]
    }
    tags: {
      labels: [
        'AI'
      ]
    }
  }
}


output appInsightsConnectionString string = appInsights.properties.ConnectionString
