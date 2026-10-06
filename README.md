# TalksOps

## Functions API security and configuration

All HTTP-triggered Functions require a Function key. Keep the key in server-side web configuration or Key Vault; never send it to browser code or publish it as an infrastructure output. Configure the Functions host with `Storage__TableServiceUri` set to the Azure Table service endpoint and `StorageTableName` set to the shared table name. The Function App uses its managed identity through `DefaultAzureCredential` to access Tables. Events and proposals share that table, with `user|...` and `event|...` partition-key prefixes keeping their records separate.

The API receives the user ID in its request envelope or query string. The authenticated Blazor Server application must populate that value from the signed-in principal's `sub` claim, not from editable form data. The Function key is a shared application credential, not per-user authentication: anyone who obtains it can submit a different user ID and potentially act as that user. Stronger caller identity binding requires API token validation or another per-user authentication mechanism.