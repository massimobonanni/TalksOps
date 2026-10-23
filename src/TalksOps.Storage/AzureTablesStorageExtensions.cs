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

    /// <summary>Registers repositories using separate event and token-consumption tables with managed identity.</summary>
    public static IServiceCollection AddTalksOpsAzureTables(
        this IServiceCollection services,
        Uri serviceUri,
        string eventTableName,
        string tokensConsumptionTableName = TokensConsumptionTableClientKey)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceUri);
        ValidateTableNames(eventTableName, tokensConsumptionTableName);

        var tableServiceClient = new TableServiceClient(serviceUri, new DefaultAzureCredential());
        return AddRepositories(services, tableServiceClient, eventTableName, tokensConsumptionTableName);
    }

    /// <summary>Registers repositories using a connection string, including Azurite development storage.</summary>
    public static IServiceCollection AddTalksOpsAzureTables(
        this IServiceCollection services,
        string connectionString,
        string eventTableName,
        string tokensConsumptionTableName = TokensConsumptionTableClientKey)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ValidateTableNames(eventTableName, tokensConsumptionTableName);

        var tableServiceClient = new TableServiceClient(connectionString);
        return AddRepositories(services, tableServiceClient, eventTableName, tokensConsumptionTableName);
    }

    /// <summary>Keeps repository registrations identical for both authentication methods.</summary>
    private static IServiceCollection AddRepositories(
        IServiceCollection services,
        TableServiceClient tableServiceClient,
        string eventTableName,
        string tokensConsumptionTableName)
    {
        services.AddSingleton(tableServiceClient);
        services.AddSingleton(tableServiceClient.GetTableClient(eventTableName));
        services.AddKeyedSingleton(
            TokensConsumptionTableClientKey,
            (_, _) => tableServiceClient.GetTableClient(tokensConsumptionTableName));
        services.AddSingleton<IEventRepository, AzureTableEventRepository>();
        services.AddSingleton<ISessionProposalRepository, AzureTableSessionProposalRepository>();
        services.AddSingleton<ITokensConsumptionRepository, AzureTableTokensConsumptionRepository>();

        return services;
    }

    private static void ValidateTableNames(string eventTableName, string tokensConsumptionTableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokensConsumptionTableName);
        if (string.Equals(eventTableName, tokensConsumptionTableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Event and token-consumption data must use separate Azure Tables.");
        }
    }
}