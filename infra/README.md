# TalksOps infrastructure

The AZD project provisions a resource group-scoped Bicep deployment in `infra/main.bicep`. It creates a Linux App Service for the Blazor Server BFF, a .NET 10 Flex Consumption Function App, separate StorageV2 accounts for application data (`datastore`) and Functions host/deployment data (`funcstore`), a Key Vault, and workspace-based Application Insights.

## Modules and access

- `modules/storage.bicep` creates the application data storage account and the `TalksOps` table. Shared-key access is disabled.
- `modules/backend.bicep` creates a separate Functions storage account and private deployment package container, then creates the .NET isolated 10 Flex Consumption app. `AzureWebJobsStorage__accountName` and deployment storage point to this account; both use the Function App's system-assigned managed identity.
- `modules/frontend.bicep` creates the Linux App Service, which uses Key Vault references for both authentication and the Function key.
- `modules/keyvault.bicep` creates the RBAC-mode Key Vault and the `function-api-key` secret. It reads the generated default host key from the Function App with the ARM `listKeys` function and writes that value directly to the vault; the key is not a Bicep output or parameter.
- `modules/monitoring.bicep` creates Log Analytics and workspace-based Application Insights.
- `modules/identity.bicep` grants the Function identity Storage Blob Data Owner on `funcstore` for host and deployment storage, Storage Table Data Contributor scoped only to the `TalksOps` table in `datastore`, and grants the Web identity Key Vault Secrets User. No Queue role is assigned because the current Functions app has no queue binding; add one only if a future trigger or binding requires it. The Key Vault module grants the AZD deployment principal Key Vault Secrets Officer so Bicep can create the Function-key secret and the post-provision hook can add the web client secret.

The web app registration is tenant-scoped and therefore handled by the AZD `postprovision` hook, not the resource-group Bicep deployment. The hook creates or updates a `AzureADandPersonalMicrosoftAccount` registration, its service principal, and the production `/signin-oidc` redirect URI. It creates a two-year client credential only if `web-client-secret` is not already present, stores that credential in Key Vault, and configures App Service with a Key Vault reference. The Function key is created by the Functions resource provider and copied into Key Vault by Bicep during provisioning. Neither secret is a Bicep parameter or output.

## Application data partitions

Events and session proposals share the `TalksOps` table in the application data storage account:

| Record | Partition key | Row key |
| --- | --- | --- |
| Event | `events\|{ownerId}` | Event GUID in `N` format (32 hexadecimal digits, no hyphens) |
| Session proposal | `sessions\|{eventId:N}` | Proposal GUID in `N` format |

Queries, ownership checks, updates, and cascading proposal deletion use these same partition keys. Session proposals also retain their `OwnerId` property for owner filtering.


## Prerequisites

- .NET 10 SDK, Azure CLI, Azure Developer CLI, and Bicep CLI.
- An Azure subscription and region in which the selected App Service, Flex Consumption, Storage, Key Vault, and monitoring resources are available.
- AZD permissions to create the resource group resources and role assignments. The deployment identity is granted Key Vault Secrets Officer on the new vault by Bicep.
- Microsoft Graph permission to create and update applications and service principals in the selected Entra tenant. Tenant policy may require administrator approval. The application accepts personal Microsoft accounts; it is not an App Service Easy Auth configuration.
- The deployer must be able to create service principals and credentials through Microsoft Graph. Client credentials expire after two years; rotate the Key Vault secret and app registration credential before expiry.
- Before provisioning, set the object ID and type of the identity that runs AZD. This identity must be allowed to create role assignments and will receive Key Vault Secrets Officer on the new vault:

```powershell
azd env set AZURE_PRINCIPAL_ID (az ad signed-in-user show --query id --output tsv)
azd env set AZURE_PRINCIPAL_TYPE User
```

## Provision and deploy

No Azure environment is selected or deployed by this repository change. Choose the tenant, subscription, region, and environment before running any cloud-changing command:

```powershell
az login
azd auth login
azd env new talksops-dev
azd env set AZURE_SUBSCRIPTION_ID <subscription-id>
azd env set AZURE_LOCATION <azure-region>
azd up -e talksops-dev
```

`azd up` first provisions Bicep, which creates the Function host key and the corresponding Key Vault secret, then runs the identity setup hook and deploys the API and web app. The hook uses the current Azure CLI identity for Microsoft Graph and Key Vault operations. If Graph permissions or Key Vault RBAC have not propagated, address the reported prerequisite and rerun the relevant hook; do not paste secrets into AZD outputs or source files.

For a provisioning-only review, run `azd provision -e talksops-dev` after choosing the environment and confirming the target subscription, tenant, and region. A `what-if` or a real provisioning operation is intentionally not run as part of local implementation.

## Local validation and cleanup

```powershell
az bicep build --file infra/main.bicep
azd package
```

Review the deployment plan and outputs before provisioning. `API_ENDPOINT_URL`, `WEB_ENDPOINT_URL`, resource names, and the app-registration display name are nonsecret outputs. `.azure/`, local Function settings, Terraform state, and plan files are ignored by Git.

Remove an environment only after confirming its resource group and data-retention requirements:

```powershell
azd down -e talksops-dev
```

## Known deployment boundary

Entra app registrations are tenant objects, while the Bicep deployment is scoped to an Azure resource group. The hook deliberately keeps these operations separate. Validate Graph consent, tenant policy, AZD hook behavior, and Key Vault RBAC in a disposable environment before adopting this automation in a production tenant. The shared Function key remains an application credential: possession of it allows callers to assert a different payload user ID, as documented in the root README.