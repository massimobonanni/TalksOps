#!/usr/bin/env sh
set -eu

app_name=${AZURE_AD_APP_DISPLAY_NAME:-}
client_id=${AZURE_AD_APP_CLIENT_ID:-}
if [ -z "$client_id" ]; then
  if [ -n "$app_name" ]; then
    client_ids=$(az ad app list --display-name "$app_name" --query '[].appId' --output tsv)
  elif [ -n "${AZURE_ENV_NAME:-}" ]; then
    app_name="TalksOps-${AZURE_ENV_NAME}-"
    query="[?starts_with(displayName, '$app_name')].appId"
    client_ids=$(az ad app list --query "$query" --output tsv)
  else
    printf '%s\n' "Neither 'AZURE_AD_APP_CLIENT_ID', 'AZURE_AD_APP_DISPLAY_NAME', nor 'AZURE_ENV_NAME' is available to identify the frontend app registration." >&2
    exit 1
  fi

  if [ -z "$client_ids" ] || [ "$client_ids" = 'None' ]; then
    printf "No frontend app registration matching '%s' was found; nothing to remove.\n" "$app_name"
    exit 0
  fi

  client_id_count=$(printf '%s\n' "$client_ids" | awk 'NF { count++ } END { print count + 0 }')
  if [ "$client_id_count" -ne 1 ]; then
    printf "Expected one frontend app registration matching '%s', found %s; refusing to delete an ambiguous identity.\n" "$app_name" "$client_id_count" >&2
    exit 1
  fi

  client_id=$client_ids
fi

if az ad sp show --id "$client_id" --query id --output tsv >/dev/null 2>&1; then
  az ad sp delete --id "$client_id" --output none
fi

az ad app delete --id "$client_id" --output none
printf "Removed the frontend app registration '%s'.\n" "$app_name"