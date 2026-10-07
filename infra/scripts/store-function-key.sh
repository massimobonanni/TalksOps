#!/usr/bin/env sh
set -eu

for name in AZURE_RESOURCE_GROUP AZURE_FUNCTION_APP_NAME AZURE_KEY_VAULT_NAME AZURE_WEB_APP_NAME; do
  eval "value=\${$name-}"
  if [ -z "$value" ]; then
    printf "Required AZD output '%s' is missing.\n" "$name" >&2
    exit 1
  fi
done

function_key=$(az functionapp keys list --resource-group "$AZURE_RESOURCE_GROUP" --name "$AZURE_FUNCTION_APP_NAME" --query 'functionKeys.default' --output tsv)
[ -n "$function_key" ] || { printf '%s\n' 'Unable to retrieve the deployed Function App default host key.' >&2; exit 1; }
az keyvault secret set --vault-name "$AZURE_KEY_VAULT_NAME" --name function-api-key --value "$function_key" --output none
unset function_key
az webapp restart --resource-group "$AZURE_RESOURCE_GROUP" --name "$AZURE_WEB_APP_NAME" --output none
printf '%s\n' 'Stored the Function App key in Key Vault.'