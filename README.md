![TalksOps](src/TalksOps.Web/wwwroot/images/TalksOps-logo.svg)

TalksOps is a .NET 10 application for managing speaking events, session proposals, and an annual calendar. A server-side Blazor application calls an Azure Functions API; Azure Tables stores application data.

## Documentation

- [Source code guide and class diagrams](src/README.md)
- [Infrastructure, architecture, and provisioning guide](infra/README.md)
- [Core domain and business services](src/TalksOps.Core/README.md)
- [REST contracts and HTTP client](src/TalksOps.ApiClient/README.md)
- [Azure Tables persistence](src/TalksOps.Storage/README.md)
- [Functions API](src/TalksOps.Functions/README.md)
- [Blazor Server web application](src/TalksOps.Web/README.md)

## Deploy with Azure Developer CLI

Install the latest stable version of the [Azure Developer CLI (`azd`)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) before deploying. The latest stable release is **1.35.1** (checked October 13, 2026); this repository does not pin a minimum version. Confirm the installed version with `azd version`. You also need the .NET 10 SDK, Azure CLI, and Bicep CLI; see the [infrastructure guide](infra/README.md) for Azure permissions and other prerequisites.

Sign in, create an AZD environment, and set its required values. `AZURE_ENV_NAME` is set by `azd env new`; the deployment identity must be allowed to create role assignments.

```powershell
az login
azd auth login
azd env new talksops-dev
azd env set AZURE_PROJECT_NAME talksops-dev
azd env set AZURE_SUBSCRIPTION_ID <subscription-id>
azd env set AZURE_LOCATION <azure-region>
```

Required environment values are `AZURE_ENV_NAME` (created with the environment), `AZURE_PROJECT_NAME`, `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION`, and `AZURE_PRINCIPAL_ID` (the deployment identity's object ID).

Provision the Azure resources and deploy both services with `azd up`. For subsequent code-only updates, use `azd deploy`:

```powershell
azd up -e talksops-dev
azd deploy -e talksops-dev
```

> [!NOTE]
> If you receive an error during `azd up` related to a resource that already exists, try again the command


To remove the environment, including its Azure resources and tenant app registration, run:

```powershell
azd down -e talksops-dev
```

This teardown deletes the provisioned data as well. Review the target subscription and data-retention requirements first. For the full deployment lifecycle, hook behavior, and required permissions, see the [infrastructure guide](infra/README.md).

## Architecture

```mermaid
flowchart LR
	Browser[Browser] --> Web[Blazor Server BFF]
	Web -->|OpenID Connect| Identity[Microsoft identity platform]
	Web -->|Server-side HTTP and Function key| Api[Azure Functions API]
	Api --> Core[Core business services]
	Core --> Storage[Storage repositories]
	Storage -->|Managed identity in Azure| Tables[Azure Tables]
	Vault[Key Vault] -->|Managed identity and secret references| Web
	Web -.-> Insights[Application Insights]
	Api -.-> Insights
```

## Functions API security and configuration

All HTTP-triggered Functions require a Function key. Keep the key in server-side web configuration or Key Vault; never send it to browser code or publish it as an infrastructure output. Configure the Functions host with `StorageUri` set to the Azure Table service endpoint and `StorageTableName` set to the shared table name. The Function App uses its managed identity through `DefaultAzureCredential` to access Tables. Events and session proposals share that table: events use `events|{ownerId}` partition keys, and session proposals use `sessions|{eventId}` partition keys, where `eventId` is a GUID formatted as 32 hexadecimal digits without hyphens. Row keys remain the event or proposal GUID in the same format.

### Local Functions development

Install Azure Functions Core Tools v4 and Azurite, then start Azurite with its Blob, Queue, and Table services enabled. The Functions project's `local.settings.json` uses these values:

```json
{
	"IsEncrypted": false,
	"Values": {
		"AzureWebJobsStorage": "UseDevelopmentStorage=true",
		"FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
		"StorageTableName": "TalksOps",
		"StorageConnectionString": "UseDevelopmentStorage=true"
	}
}
```

From `src/TalksOps.Functions`, run `dotnet run`. When `StorageConnectionString` is configured, the application uses shared-key authentication and creates the application table if it does not exist. Azurite must be running before the Functions start. The standard development connection string uses the Table endpoint at `http://127.0.0.1:10002/devstoreaccount1`; use a custom connection string if your emulator has different endpoints.

`StorageConnectionString` takes precedence over `StorageUri`. Leave it unset in Azure to retain `DefaultAzureCredential` authentication. Do not commit connection strings containing real account keys.

The API receives the user ID in its request envelope or query string. The authenticated Blazor Server application must populate that value from the signed-in principal's `sub` claim, not from editable form data. The Function key is a shared application credential, not per-user authentication: anyone who obtains it can submit a different user ID and potentially act as that user. Stronger caller identity binding requires API token validation or another per-user authentication mechanism.