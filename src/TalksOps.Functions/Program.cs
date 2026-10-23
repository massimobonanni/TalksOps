using System.Globalization;
using Azure.Data.Tables;
using System.Threading.RateLimiting;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TalksOps.AI;
using TalksOps.AI.Providers;
using TalksOps.AI.Services;
using TalksOps.Core.Contracts;
using TalksOps.Core.Extraction;
using TalksOps.Core.Services;
using TalksOps.Functions;
using TalksOps.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>(
    typeof(EventDataExtractionService).FullName!,
    LogLevel.Information);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddScoped<RequestCurrentUserContext>();
builder.Services.AddScoped<ICurrentUserContext>(services => services.GetRequiredService<RequestCurrentUserContext>());
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ISessionProposalService, SessionProposalService>();
builder.Services.AddScoped<IEventImportExportService, EventImportExportService>();
var extractionRateLimitPermitLimit = int.Parse(
    builder.Configuration["EventDataExtraction:RateLimit:PermitLimit"] ?? "1",
    NumberStyles.None,
    CultureInfo.InvariantCulture);
var extractionRateLimitWindowSeconds = int.Parse(
    builder.Configuration["EventDataExtraction:RateLimit:WindowSeconds"] ?? "60",
    NumberStyles.None,
    CultureInfo.InvariantCulture);
if (extractionRateLimitPermitLimit < 1 || extractionRateLimitWindowSeconds < 1)
{
    throw new InvalidOperationException("Event extraction rate-limit values must be greater than zero.");
}

builder.Services.AddSingleton<PartitionedRateLimiter<string>>(_ =>
    PartitionedRateLimiter.Create<string, string>(userId =>
        RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = extractionRateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(extractionRateLimitWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            })));

var foundryEndpoint = builder.Configuration["FoundryEndpoint"];
var foundryProjectName = builder.Configuration["FoundryProjectName"];
var foundryDeploymentName = builder.Configuration["FoundryDeploymentName"];
if (Uri.TryCreate(foundryEndpoint, UriKind.Absolute, out var parsedFoundryEndpoint)
    && !string.IsNullOrWhiteSpace(foundryProjectName)
    && !string.IsNullOrWhiteSpace(foundryDeploymentName))
{
    builder.Services.AddSingleton<IEventPageFetcher, PublicEventPageFetcher>();
    builder.Services.AddSingleton<IEventHtmlCleaner, ReverseMarkdownEventHtmlCleaner>();
    builder.Services.AddSingleton<IEventDataChatClient>(new FoundryEventDataChatClient(
        parsedFoundryEndpoint,
        foundryProjectName,
        foundryDeploymentName));
    builder.Services.AddScoped<IEventDataExtractionService, EventDataExtractionService>();
}

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
