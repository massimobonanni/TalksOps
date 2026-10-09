#!/usr/bin/env sh
set -eu

if [ -z "${AZURE_AD_APP_DISPLAY_NAME:-}" ]; then
  printf '%s\n' "Required AZD output 'AZURE_AD_APP_DISPLAY_NAME' is missing." >&2
  exit 1
fi

app_name=$AZURE_AD_APP_DISPLAY_NAME
client_ids=$(az ad app list --display-name "$app_name" --query '[].appId' --output tsv)
if [ -z "$client_ids" ] || [ "$client_ids" = 'None' ]; then
  printf "No app registration named '%s' was found; nothing to remove.\n" "$app_name"
  exit 0
fi

client_id_count=$(printf '%s\n' "$client_ids" | awk 'NF { count++ } END { print count + 0 }')
if [ "$client_id_count" -ne 1 ]; then
  printf "Expected one app registration named '%s', found %s; refusing to delete an ambiguous identity.\n" "$app_name" "$client_id_count" >&2
  exit 1
fi

client_id=$client_ids
if az ad sp show --id "$client_id" --query id --output tsv >/dev/null 2>&1; then
  az ad sp delete --id "$client_id" --output none
fi

az ad app delete --id "$client_id" --output none
printf "Removed the frontend app registration '%s'.\n" "$app_name"