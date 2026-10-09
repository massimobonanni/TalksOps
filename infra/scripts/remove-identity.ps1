$ErrorActionPreference = 'Stop'

$appName = $env:AZURE_AD_APP_DISPLAY_NAME
$clientId = $env:AZURE_AD_APP_CLIENT_ID
if ([string]::IsNullOrWhiteSpace($clientId)) {
    if (-not [string]::IsNullOrWhiteSpace($appName)) {
        $clientIds = @(az ad app list --display-name $appName --query '[].appId' --output tsv)
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:AZURE_ENV_NAME)) {
        $prefix = "TalksOps-$($env:AZURE_ENV_NAME)-"
        $query = "[?starts_with(displayName, '$prefix')].appId"
        $clientIds = @(az ad app list --query $query --output tsv)
        $appName = $prefix
    }
    else {
        throw "Neither 'AZURE_AD_APP_CLIENT_ID', 'AZURE_AD_APP_DISPLAY_NAME', nor 'AZURE_ENV_NAME' is available to identify the frontend app registration."
    }

    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to find the frontend app registration.'
    }
    $clientIds = @($clientIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne 'None' })
    if ($clientIds.Count -eq 0) {
        Write-Output "No frontend app registration matching '$appName' was found; nothing to remove."
        exit 0
    }
    if ($clientIds.Count -ne 1) {
        throw "Expected one frontend app registration matching '$appName', found $($clientIds.Count); refusing to delete an ambiguous identity."
    }
    $clientId = $clientIds[0]
}

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