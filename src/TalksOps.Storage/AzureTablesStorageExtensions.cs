using Azure.Identity;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.Core.Contracts;

namespace TalksOps.Storage;

/// <summary>Registers Azure Tables persistence services.</summary>
public static class AzureTablesStorageExtensions
{
    /// <summary>Registers repositories using one configured table and managed identity credentials.</summary>
    public static IServiceCollection AddTalksOpsAzureTables(
        this IServiceCollection services,
        Uri serviceUri,
        string tableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var tableServiceClient = new TableServiceClient(serviceUri, new DefaultAzureCredential());
        return AddRepositories(services, tableServiceClient, tableName);
    }

    /// <summary>Registers repositories using a connection string, including Azurite development storage.</summary>
    public static IServiceCollection AddTalksOpsAzureTables(
        this IServiceCollection services,
        string connectionString,
        string tableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var tableServiceClient = new TableServiceClient(connectionString);
        return AddRepositories(services, tableServiceClient, tableName);
    }

    /// <summary>Keeps repository registrations identical for both authentication methods.</summary>
    private static IServiceCollection AddRepositories(
        IServiceCollection services,
        TableServiceClient tableServiceClient,
        string tableName)
    {
        services.AddSingleton(tableServiceClient);
        services.AddSingleton(tableServiceClient.GetTableClient(tableName));
        services.AddSingleton<IEventRepository, AzureTableEventRepository>();
        services.AddSingleton<ISessionProposalRepository, AzureTableSessionProposalRepository>();

        return services;
    }
}