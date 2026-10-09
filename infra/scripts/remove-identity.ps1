$ErrorActionPreference = 'Stop'

$appName = $env:AZURE_AD_APP_DISPLAY_NAME
if ([string]::IsNullOrWhiteSpace($appName)) {
    throw "Required AZD output 'AZURE_AD_APP_DISPLAY_NAME' is missing."
}

$clientIds = @(az ad app list --display-name $appName --query '[].appId' --output tsv)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to find the frontend app registration.'
}
$clientIds = @($clientIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne 'None' })
if ($clientIds.Count -eq 0) {
    Write-Output "No app registration named '$appName' was found; nothing to remove."
    exit 0
}
if ($clientIds.Count -ne 1) {
    throw "Expected one app registration named '$appName', found $($clientIds.Count); refusing to delete an ambiguous identity."
}

$clientId = $clientIds[0]
az ad sp show --id $clientId --query id --output tsv *> $null
if ($LASTEXITCODE -eq 0) {
    az ad sp delete --id $clientId --output none
    if ($LASTEXITCODE -ne 0) {
        throw 'Removing the frontend app registration service principal failed.'
    }
}

az ad app delete --id $clientId --output none
if ($LASTEXITCODE -ne 0) {
    throw 'Removing the frontend app registration failed.'
}

Write-Output "Removed the frontend app registration '$appName'."