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

var tableServiceUri = builder.Configuration["StorageUri"];
if (!Uri.TryCreate(tableServiceUri, UriKind.Absolute, out var parsedTableServiceUri))
{
    throw new InvalidOperationException("The StorageUri setting must contain the Azure Tables endpoint URI.");
}

var tableName = builder.Configuration["StorageTableName"];
if (string.IsNullOrWhiteSpace(tableName))
{
    throw new InvalidOperationException("The StorageTableName setting must contain the shared Azure Table name.");
}

builder.Services.AddTalksOpsAzureTables(parsedTableServiceUri, tableName);

builder.Build().Run();
