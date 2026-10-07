#!/usr/bin/env sh
set -eu

for name in AZURE_ENV_NAME AZURE_RESOURCE_GROUP AZURE_AD_APP_DISPLAY_NAME WEB_ENDPOINT_URL AZURE_KEY_VAULT_NAME; do
  eval "value=\${$name-}"
  if [ -z "$value" ]; then
    printf "Required AZD output '%s' is missing.\n" "$name" >&2
    exit 1
  fi
done

app_name=$AZURE_AD_APP_DISPLAY_NAME
redirect_uri="${WEB_ENDPOINT_URL%/}/signin-oidc"
client_id=$(az ad app list --display-name "$app_name" --query '[0].appId' --output tsv)
if [ -z "$client_id" ] || [ "$client_id" = "None" ]; then
  client_id=$(az ad app create --display-name "$app_name" --sign-in-audience AzureADandPersonalMicrosoftAccount --web-redirect-uris "$redirect_uri" --query appId --output tsv)
else
  az ad app update --id "$client_id" --sign-in-audience AzureADandPersonalMicrosoftAccount --web-redirect-uris "$redirect_uri" --output none
fi
[ -n "$client_id" ] || { printf '%s\n' 'Creating or updating the personal-account app registration failed.' >&2; exit 1; }

if ! az ad sp show --id "$client_id" --query id --output tsv >/dev/null 2>&1; then
  az ad sp create --id "$client_id" --output none
fi

secret_count=$(az keyvault secret list --vault-name "$AZURE_KEY_VAULT_NAME" --query "[?name=='web-client-secret'] | length(@)" --output tsv)
if [ "$secret_count" = '0' ]; then
  client_secret=$(az ad app credential reset --id "$client_id" --append --display-name talksops-web --years 2 --query password --output tsv)
  [ -n "$client_secret" ] || { printf '%s\n' 'Creating the app registration client secret failed.' >&2; exit 1; }
  az keyvault secret set --vault-name "$AZURE_KEY_VAULT_NAME" --name web-client-secret --value "$client_secret" --output none
  unset client_secret
fi

secret_uri="https://${AZURE_KEY_VAULT_NAME}.vault.azure.net/secrets/web-client-secret"
az webapp config appsettings set --resource-group "$AZURE_RESOURCE_GROUP" --name "$AZURE_WEB_APP_NAME" --settings "AzureAd__ClientId=$client_id" "AzureAd__ClientSecret=@Microsoft.KeyVault(SecretUri=$secret_uri)" --output none
printf 'Configured personal-account sign-in for %s.\n' "$WEB_ENDPOINT_URL"