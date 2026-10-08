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

        Assert.Same(services, services.AddTalksOpsAzureTables("UseDevelopmentStorage=true", "TalksOps"));

        var service = Assert.IsType<TableServiceClient>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TableServiceClient)).ImplementationInstance);
        var table = Assert.IsType<TableClient>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TableClient)).ImplementationInstance);
        Assert.Equal(new Uri("http://127.0.0.1:10002/devstoreaccount1"), service.Uri);
        Assert.Equal("TalksOps", table.Name);
        Assert.Equal(typeof(AzureTableEventRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEventRepository)).ImplementationType);
        Assert.Equal(typeof(AzureTableSessionProposalRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISessionProposalRepository)).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    /// <summary>Verifies the Azure endpoint registration remains supported.</summary>
    [Fact]
    public void ServiceUri_RegistersAzureClients()
    {
        var services = new ServiceCollection();
        var serviceUri = new Uri("https://talksops.table.core.windows.net");

        services.AddTalksOpsAzureTables(serviceUri, "TalksOps");

        var service = Assert.IsType<TableServiceClient>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TableServiceClient)).ImplementationInstance);
        var table = Assert.IsType<TableClient>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TableClient)).ImplementationInstance);
        Assert.Equal(serviceUri, service.Uri);
        Assert.Equal("TalksOps", table.Name);
        Assert.Equal(typeof(AzureTableEventRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEventRepository)).ImplementationType);
        Assert.Equal(typeof(AzureTableSessionProposalRepository),
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISessionProposalRepository)).ImplementationType);
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
}