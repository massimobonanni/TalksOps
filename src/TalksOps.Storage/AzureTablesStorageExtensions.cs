using Azure.Identity;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.Core.Contracts;

namespace TalksOps.Storage;

/// <summary>Registers Azure Tables persistence services.</summary>
public static class AzureTablesStorageExtensions
{
    /// <summary>The dependency-injection key for the token-consumption table client.</summary>
    public const string TokensConsumptionTableClientKey = "TokensConsumption";

    /// <summary>The default Azure Table name used to store session feedback.</summary>
    public const string StorageFeedbackTableName = "SessionsFeedback";

    /// <summary>The dependency-injection key for the session-feedback table client.</summary>
    public const string FeedbackTableClientKey = "SessionsFeedback";

    /// <summary>Registers repositories using separate event and token-consumption tables with managed identity.</summary>
    public static IServiceCollection AddTalksOpsAzureTables(
        this IServiceCollection services,
        Uri serviceUri,
        string eventTableName,
        string tokensConsumptionTableName = TokensConsumptionTableClientKey,
        string feedbackTableName = StorageFeedbackTableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceUri);
        ValidateTableNames(eventTableName, tokensConsumptionTableName, feedbackTableName);

        var tableServiceClient = new TableServiceClient(serviceUri, new DefaultAzureCredential());
        return AddRepositories(services, tableServiceClient, eventTableName, tokensConsumptionTableName, feedbackTableName);
    }

    /// <summary>Registers repositories using a connection string, including Azurite development storage.</summary>
    public static IServiceCollection AddTalksOpsAzureTables(
        this IServiceCollection services,
        string connectionString,
        string eventTableName,
        string tokensConsumptionTableName = TokensConsumptionTableClientKey,
        string feedbackTableName = StorageFeedbackTableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ValidateTableNames(eventTableName, tokensConsumptionTableName, feedbackTableName);

        var tableServiceClient = new TableServiceClient(connectionString);
        return AddRepositories(services, tableServiceClient, eventTableName, tokensConsumptionTableName, feedbackTableName);
    }

    /// <summary>Keeps repository registrations identical for both authentication methods.</summary>
    private static IServiceCollection AddRepositories(
        IServiceCollection services,
        TableServiceClient tableServiceClient,
        string eventTableName,
        string tokensConsumptionTableName,
        string feedbackTableName)
    {
        services.AddSingleton(tableServiceClient);
        services.AddSingleton(tableServiceClient.GetTableClient(eventTableName));
        services.AddKeyedSingleton(
            TokensConsumptionTableClientKey,
            (_, _) => tableServiceClient.GetTableClient(tokensConsumptionTableName));
        services.AddKeyedSingleton(
            FeedbackTableClientKey,
            (_, _) => tableServiceClient.GetTableClient(feedbackTableName));
        services.AddSingleton<IEventRepository, AzureTableEventRepository>();
        services.AddSingleton<ISessionProposalRepository, AzureTableSessionProposalRepository>();
        services.AddSingleton<ITokensConsumptionRepository, AzureTableTokensConsumptionRepository>();
        services.AddSingleton<IFeedbackService, AzureTableStorageFeedbackService>();

        return services;
    }

    private static void ValidateTableNames(
        string eventTableName,
        string tokensConsumptionTableName,
        string feedbackTableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokensConsumptionTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedbackTableName);
        if (new[] { eventTableName, tokensConsumptionTableName, feedbackTableName }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != 3)
        {
            throw new ArgumentException("Event, token-consumption, and feedback data must use separate Azure Tables.");
        }
    }
}