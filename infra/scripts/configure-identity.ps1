$ErrorActionPreference = 'Stop'

$required = @(
    'AZURE_ENV_NAME',
    'AZURE_RESOURCE_GROUP',
    'AZURE_AD_APP_DISPLAY_NAME',
    'WEB_ENDPOINT_URL',
    'AZURE_KEY_VAULT_NAME'
)
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required AZD output '$name' is missing."
    }
}

$appName = $env:AZURE_AD_APP_DISPLAY_NAME
$redirectUri = "$($env:WEB_ENDPOINT_URL.TrimEnd('/'))/signin-oidc"
$clientId = az ad app list --display-name $appName --query '[0].appId' --output tsv
if ([string]::IsNullOrWhiteSpace($clientId)) {
    $clientId = az ad app create --display-name $appName --sign-in-audience PersonalMicrosoftAccount --web-redirect-uris $redirectUri --enable-id-token-issuance true --query appId --output tsv
}
else {
    az ad app update --id $clientId --sign-in-audience PersonalMicrosoftAccount --web-redirect-uris $redirectUri --enable-id-token-issuance true --output none
}
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($clientId)) {
    throw 'Creating or updating the Microsoft personal-account app registration failed.'
}

az ad sp show --id $clientId --query id --output tsv *> $null
if ($LASTEXITCODE -ne 0) {
    az ad sp create --id $clientId --output none
    if ($LASTEXITCODE -ne 0) {
        throw 'Creating the app registration service principal failed.'
    }
}

    azd env set AZURE_AD_APP_CLIENT_ID $clientId
    if ($LASTEXITCODE -ne 0) {
        throw 'Persisting the frontend app registration ID in the AZD environment failed.'
    }

$secretExists = az keyvault secret list --vault-name $env:AZURE_KEY_VAULT_NAME --query "[?name=='web-client-secret'] | length(@)" --output tsv
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to check the web client secret in Key Vault. Verify Key Vault Secrets Officer access and RBAC propagation.'
}
if ($secretExists -eq '0') {
    $clientSecret = az ad app credential reset --id $clientId --append --display-name talksops-web --years 2 --query password --output tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($clientSecret)) {
        throw 'Creating the app registration client secret failed.'
    }
    az keyvault secret set --vault-name $env:AZURE_KEY_VAULT_NAME --name web-client-secret --value $clientSecret --output none
    if ($LASTEXITCODE -ne 0) {
        throw 'Storing the web client secret in Key Vault failed.'
    }
    Remove-Variable clientSecret
}

$secretUri = "https://$($env:AZURE_KEY_VAULT_NAME).vault.azure.net/secrets/web-client-secret"
$settingsPath = [System.IO.Path]::GetTempFileName()
try {
    @{
        AzureAd__ClientId = $clientId
        AzureAd__ClientSecret = "@Microsoft.KeyVault(SecretUri=$secretUri)"
    } | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

    az webapp config appsettings set --resource-group $env:AZURE_RESOURCE_GROUP --name $env:AZURE_WEB_APP_NAME --settings "@$settingsPath" --output none
    if ($LASTEXITCODE -ne 0) {
        throw 'Configuring the Web App OpenID Connect settings failed.'
    }
}
finally {
    Remove-Item -LiteralPath $settingsPath -Force
}

Write-Output "Configured personal-account sign-in for $($env:WEB_ENDPOINT_URL)."