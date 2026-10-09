using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TalksOps.Core.Contracts;
using TalksOps.Core.Services;
using TalksOps.Functions;
using TalksOps.Storage;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddScoped<RequestCurrentUserContext>();
builder.Services.AddScoped<ICurrentUserContext>(services => services.GetRequiredService<RequestCurrentUserContext>());
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ISessionProposalService, SessionProposalService>();
builder.Services.AddScoped<IEventImportExportService, EventImportExportService>();

var tableName = builder.Configuration["StorageTableName"];
if (string.IsNullOrWhiteSpace(tableName))
{
    throw new InvalidOperationException("The StorageTableName setting must contain the shared Azure Table name.");
}

var storageConnectionString = builder.Configuration["StorageConnectionString"];
if (!string.IsNullOrWhiteSpace(storageConnectionString))
{
    builder.Services.AddTalksOpsAzureTables(storageConnectionString, tableName);
}
else
{
    var tableServiceUri = builder.Configuration["StorageUri"];
    if (!Uri.TryCreate(tableServiceUri, UriKind.Absolute, out var parsedTableServiceUri))
    {
        throw new InvalidOperationException("Configure StorageConnectionString for local storage or StorageUri with the Azure Tables endpoint URI.");
    }

    builder.Services.AddTalksOpsAzureTables(parsedTableServiceUri, tableName);
}

using var host = builder.Build();
if (!string.IsNullOrWhiteSpace(storageConnectionString))
{
    await host.Services.GetRequiredService<TableClient>().CreateIfNotExistsAsync();
}

await host.RunAsync();
