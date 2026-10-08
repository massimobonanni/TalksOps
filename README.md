# TalksOps

TalksOps is a .NET 10 application for managing speaking events, session proposals, and an annual calendar. A server-side Blazor application calls an Azure Functions API; Azure Tables stores application data.

## Documentation

- [Source code guide and class diagrams](src/README.md)
- [Infrastructure, architecture, and provisioning guide](infra/README.md)
- [Core domain and business services](src/TalksOps.Core/README.md)
- [REST contracts and HTTP client](src/TalksOps.ApiClient/README.md)
- [Azure Tables persistence](src/TalksOps.Storage/README.md)
- [Functions API](src/TalksOps.Functions/README.md)
- [Blazor Server web application](src/TalksOps.Web/README.md)

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

Existing records using the previous partition prefixes must be migrated before deploying this version; the application does not migrate or read legacy partitions automatically. See the [storage migration notes](infra/README.md#application-data-partitions).

The API receives the user ID in its request envelope or query string. The authenticated Blazor Server application must populate that value from the signed-in principal's `sub` claim, not from editable form data. The Function key is a shared application credential, not per-user authentication: anyone who obtains it can submit a different user ID and potentially act as that user. Stronger caller identity binding requires API token validation or another per-user authentication mechanism.