@description('Azure region for the Foundry account, project, and model deployment.')
param location string

@description('Microsoft Foundry account name.')
param foundryAccountName string

@description('Common resource tags.')
param tags object

var projectName = 'c4pextraction'
var deploymentName = 'gpt-4.1-nano'

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryAccountName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  tags: tags
  properties: {
    allowProjectManagement: true
    customSubDomainName: foundryAccountName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: projectName
  location: location
  tags: tags
  properties: {
    description: 'TalksOps event data extraction'
    displayName: 'TalksOps event data extraction'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: deploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: deploymentName
      version: '2025-04-14'
    }
  }
}

output accountName string = foundryAccount.name
output projectName string = foundryProject.name
output deploymentName string = modelDeployment.name
output endpoint string = 'https://${foundryAccount.name}.services.ai.azure.com'
