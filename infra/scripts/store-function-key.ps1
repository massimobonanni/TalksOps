$ErrorActionPreference = 'Stop'

foreach ($name in @('AZURE_RESOURCE_GROUP', 'AZURE_FUNCTION_APP_NAME', 'AZURE_KEY_VAULT_NAME', 'AZURE_WEB_APP_NAME')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required AZD output '$name' is missing."
    }
}

$functionKey = az functionapp keys list --resource-group $env:AZURE_RESOURCE_GROUP --name $env:AZURE_FUNCTION_APP_NAME --query 'functionKeys.default' --output tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($functionKey)) {
    throw 'Unable to retrieve the deployed Function App default host key.'
}

az keyvault secret set --vault-name $env:AZURE_KEY_VAULT_NAME --name function-api-key --value $functionKey --output none
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to store the Function App key in Key Vault. Verify Key Vault Secrets Officer access.'
}
Remove-Variable functionKey
az webapp restart --resource-group $env:AZURE_RESOURCE_GROUP --name $env:AZURE_WEB_APP_NAME --output none
if ($LASTEXITCODE -ne 0) {
    throw 'The Web App could not be restarted to refresh its Key Vault references.'
}
Write-Output 'Stored the Function App key in Key Vault.'