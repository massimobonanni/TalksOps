using Azure.Data.Tables;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Storage;

namespace TalksOps.Storage.Tests;

public sealed class AzuriteRepositoryIntegrationTests
{
    [AzuriteFact]
    public async Task Repositories_PersistOwnershipAndCascadeWithAzurite()
    {
        var connectionString = Environment.GetEnvironmentVariable("AZURITE_TABLE_CONNECTION_STRING")!;
        var table = new TableClient(connectionString, $"t{Guid.NewGuid():N}");
        await table.CreateIfNotExistsAsync();

        try
        {
            var events = new AzureTableEventRepository(table);
            var proposals = new AzureTableSessionProposalRepository(table);
            var eventValue = await events.CreateAsync(new Event
            {
                OwnerId = "integration-owner",
                Name = "Azurite conference",
                Location = "Local",
                OfficialWebsiteUrl = "https://conference.example.com",
                CallForPapersUrl = "https://conference.example.com/cfp",
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 6, 2),
                Costs = new Dictionary<string, decimal> { ["Travel"] = 12.5m }
            });

            var loadedEvent = await events.GetAsync("integration-owner", eventValue.Id);
            Assert.NotNull(loadedEvent);
            Assert.Equal(eventValue.Name, loadedEvent.Name);
            Assert.Equal(eventValue.OfficialWebsiteUrl, loadedEvent.OfficialWebsiteUrl);
            Assert.Equal(eventValue.CallForPapersUrl, loadedEvent.CallForPapersUrl);
            Assert.Equal(eventValue.Costs["Travel"], loadedEvent.Costs["Travel"]);
            Assert.Null(await events.GetAsync("different-owner", eventValue.Id));
            Assert.Single((await events.SearchAsync("integration-owner", new EventSearchCriteria())).Items);
            Assert.Empty((await events.SearchAsync("different-owner", new EventSearchCriteria())).Items);

            var proposal = await proposals.CreateAsync(new SessionProposal
            {
                EventId = eventValue.Id,
                OwnerId = "integration-owner",
                Title = "Reliable storage",
                Abstract = "Table persistence",
                Notes = "Bring the demo environment."
            });

            var loadedProposal = await proposals.GetAsync("integration-owner", eventValue.Id, proposal.Id);
            Assert.Equal("Bring the demo environment.", loadedProposal!.Notes);
            Assert.Single(await proposals.ListByEventAsync("integration-owner", eventValue.Id));
            Assert.Empty(await proposals.ListByEventAsync("different-owner", eventValue.Id));
            Assert.Null(await proposals.GetAsync("different-owner", eventValue.Id, proposal.Id));

            var updated = await proposals.UpdateAsync(proposal with { Status = ProposalStatus.Accepted });
            Assert.Equal(ProposalStatus.Accepted, updated!.Status);

            Assert.True(await events.DeleteAsync("integration-owner", eventValue.Id));
            Assert.Null(await events.GetAsync("integration-owner", eventValue.Id));
            Assert.Empty(await proposals.ListByEventAsync("integration-owner", eventValue.Id));
        }
        finally
        {
            await table.DeleteAsync();
        }
    }
}

public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURITE_TABLE_CONNECTION_STRING")))
        {
            Skip = "Set AZURITE_TABLE_CONNECTION_STRING to run the Azurite integration test.";
        }
    }
}