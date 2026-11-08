using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.Core.Contracts;
using TalksOps.Storage;

namespace TalksOps.Storage.Tests;

/// <summary>Verifies storage registration without making network requests.</summary>
public sealed class AzureTablesStorageExtensionsTests
{
    /// <summary>Verifies development storage uses the Azurite endpoint and shared repositories.</summary>
    [Fact]
    public void ConnectionString_RegistersAzuriteClientsAndRepositories()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddTalksOpsAzureTables(
            "UseDevelopmentStorage=true", "TalksOps", "TokensConsumption"));

        var service = Assert.IsType<TableServiceClient>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TableServiceClient)).ImplementationInstance);
        var table = Assert.IsType<TableClient>(
            Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(TableClient) && descriptor.ServiceKey is null).ImplementationInstance);
        var consumptionDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TableClient)
            && Equals(descriptor.ServiceKey, AzureTablesStorageExtensions.TokensConsumptionTableClientKey));
        var consumptionTable = Assert.IsType<TableClient>(consumptionDescriptor.KeyedImplementationFactory!(
            null!, AzureTablesStorageExtensions.TokensConsumptionTableClientKey));
        var feedbackDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TableClient)
            && Equals(descriptor.ServiceKey, AzureTablesStorageExtensions.FeedbackTableClientKey));
        var feedbackTable = Assert.IsType<TableClient>(feedbackDescriptor.KeyedImplementationFactory!(
            null!, AzureTablesStorageExtensions.FeedbackTableClientKey));
        Assert.Equal(new Uri("http://127.0.0.1:10002/devstoreaccount1"), service.Uri);
        Assert.Equal("TalksOps", table.Name);
        Assert.Equal("TokensConsumption", consumptionTable.Name);
        Assert.Equal(AzureTablesStorageExtensions.StorageFeedbackTableName, feedbackTable.Name);
        Assert.NotSame(table, consumptionTable);
        Assert.NotSame(table, feedbackTable);
        Assert.NotNull(consumptionDescriptor.KeyedImplementationFactory);
        Assert.NotNull(feedbackDescriptor.KeyedImplementationFactory);
        Assert.Equal(typeof(AzureTableEventRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEventRepository)).ImplementationType);
        Assert.Equal(typeof(AzureTableSessionProposalRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISessionProposalRepository)).ImplementationType);
        Assert.Equal(typeof(AzureTableTokensConsumptionRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITokensConsumptionRepository)).ImplementationType);
        Assert.Equal(typeof(FeedbackService),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IFeedbackService)).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    /// <summary>Verifies the Azure endpoint registration remains supported.</summary>
    [Fact]
    public void ServiceUri_RegistersAzureClients()
    {
        var services = new ServiceCollection();
        var serviceUri = new Uri("https://talksops.table.core.windows.net");

        services.AddTalksOpsAzureTables(serviceUri, "TalksOps", "TokensConsumption");

        var service = Assert.IsType<TableServiceClient>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TableServiceClient)).ImplementationInstance);
        var table = Assert.IsType<TableClient>(
            Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(TableClient) && descriptor.ServiceKey is null).ImplementationInstance);
        var consumptionDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TableClient)
            && Equals(descriptor.ServiceKey, AzureTablesStorageExtensions.TokensConsumptionTableClientKey));
        var consumptionTable = Assert.IsType<TableClient>(consumptionDescriptor.KeyedImplementationFactory!(
            null!, AzureTablesStorageExtensions.TokensConsumptionTableClientKey));
        var feedbackDescriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(TableClient)
            && Equals(descriptor.ServiceKey, AzureTablesStorageExtensions.FeedbackTableClientKey));
        var feedbackTable = Assert.IsType<TableClient>(feedbackDescriptor.KeyedImplementationFactory!(
            null!, AzureTablesStorageExtensions.FeedbackTableClientKey));
        Assert.Equal(serviceUri, service.Uri);
        Assert.Equal("TalksOps", table.Name);
        Assert.Equal("TokensConsumption", consumptionTable.Name);
        Assert.Equal(AzureTablesStorageExtensions.StorageFeedbackTableName, feedbackTable.Name);
        Assert.Equal(typeof(AzureTableEventRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEventRepository)).ImplementationType);
        Assert.Equal(typeof(AzureTableSessionProposalRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISessionProposalRepository)).ImplementationType);
        Assert.Equal(typeof(AzureTableTokensConsumptionRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITokensConsumptionRepository)).ImplementationType);
        Assert.Equal(typeof(FeedbackService),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IFeedbackService)).ImplementationType);
    }

    /// <summary>Verifies missing connection strings are rejected before constructing a client.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ConnectionString_RejectsMissingValue(string? connectionString)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ServiceCollection().AddTalksOpsAzureTables(connectionString!, "TalksOps"));
    }

    /// <summary>Verifies missing table names are rejected for connection string registration.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ConnectionString_RejectsMissingTableName(string? tableName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ServiceCollection().AddTalksOpsAzureTables("UseDevelopmentStorage=true", tableName!));
    }

    /// <summary>Verifies event and token-consumption data cannot share a table.</summary>
    [Fact]
    public void ConnectionString_RejectsSameTableNames()
    {
        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddTalksOpsAzureTables("UseDevelopmentStorage=true", "TalksOps", "TalksOps"));
    }

    /// <summary>Verifies feedback storage cannot share a table with events or token usage.</summary>
    [Theory]
    [InlineData("TokensConsumption", "TokensConsumption")]
    [InlineData("TalksOps", "TokensConsumption")]
    public void ConnectionString_RejectsDuplicateTableNames(string feedbackTableName, string tokensTableName)
    {
        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddTalksOpsAzureTables(
                "UseDevelopmentStorage=true",
                "TalksOps",
                tokensTableName,
                feedbackTableName));
    }
}