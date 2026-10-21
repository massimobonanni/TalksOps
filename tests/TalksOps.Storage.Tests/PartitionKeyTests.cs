using Azure;
using Azure.Data.Tables;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Storage;

namespace TalksOps.Storage.Tests;

/// <summary>Verifies partition keys across repository operations without a storage service.</summary>
public sealed class PartitionKeyTests
{
    /// <summary>Verifies event persistence, parsing, queries, and updates use the event prefix.</summary>
    [Theory]
    [InlineData("owner|123", "events|owner|123")]
    [InlineData("speaker@example.com", "events|speaker@example.com")]
    [InlineData("speaker+talks@example.com", "events|speaker+talks@example.com")]
    [InlineData("speaker_example.com#EXT#@tenant.onmicrosoft.com", "events|speaker_example.com%23EXT%23@tenant.onmicrosoft.com")]
    [InlineData("speaker/name@example.com", "events|speaker%2Fname@example.com")]
    [InlineData("domain\\speaker", "events|domain%5Cspeaker")]
    [InlineData("speaker?@example.com", "events|speaker%3F@example.com")]
    [InlineData("speaker%23@example.com", "events|speaker%2523@example.com")]
    [InlineData("speaker#@example.com", "events|speaker%23@example.com")]
    [InlineData("speaker\u0001@example.com", "events|speaker%01@example.com")]
    [InlineData("speaker\u007f@example.com", "events|speaker%7F@example.com")]
    [InlineData("speaker\u009f@example.com", "events|speaker%C2%9F@example.com")]
    public async Task EventOperations_UseEventsPartitionAndPreserveOwner(string ownerId, string partitionKey)
    {
        var table = new RecordingTableClient();
        var repository = new AzureTableEventRepository(table);
        var value = new Event
        {
            OwnerId = ownerId,
            Name = "Summit",
            Location = "Rome",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 2)
        };

        await repository.CreateAsync(value);

        Assert.Equal(partitionKey, table.Entity!.PartitionKey);
        Assert.Equal(value.Id.ToString("N"), table.Entity.RowKey);

        var loaded = await repository.GetAsync(value.OwnerId, value.Id);
        Assert.Equal(value.OwnerId, loaded!.OwnerId);
        Assert.Equal(value.Id, loaded.Id);

        var results = await repository.SearchAsync(value.OwnerId, new EventSearchCriteria());
        Assert.Equal(value.OwnerId, Assert.Single(results.Items).OwnerId);
        Assert.Equal($"PartitionKey eq '{partitionKey}'", Assert.Single(table.Filters));

        Assert.NotNull(await repository.UpdateAsync(value));
        Assert.All(table.Lookups, lookup => Assert.Equal((partitionKey, value.Id.ToString("N")), lookup));
        Assert.Equal(partitionKey, table.Entity.PartitionKey);
    }

    /// <summary>Verifies proposal operations use session partitions and check the event partition.</summary>
    [Theory]
    [InlineData("owner-123", "events|owner-123")]
    [InlineData("speaker@example.com", "events|speaker@example.com")]
    [InlineData("speaker#EXT#@example.com", "events|speaker%23EXT%23@example.com")]
    public async Task ProposalOperations_UseSessionsPartitionAndPreserveEventId(string ownerId, string eventPartitionKey)
    {
        var table = new RecordingTableClient();
        var eventId = Guid.NewGuid();
        await new AzureTableEventRepository(table).CreateAsync(new Event
        {
            Id = eventId,
            OwnerId = ownerId,
            Name = "Summit",
            Location = "Rome",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 2)
        });
        var repository = new AzureTableSessionProposalRepository(table);
        var proposal = new SessionProposal
        {
            EventId = eventId,
            OwnerId = ownerId,
            Title = "Storage",
            Abstract = "Partitioning",
            SubmissionDate = new DateOnly(2026, 3, 15)
        };
        var partitionKey = $"sessions|{eventId:N}";

        await repository.CreateAsync(proposal);

        Assert.Equal(partitionKey, table.Entity!.PartitionKey);
        Assert.Equal(proposal.Id.ToString("N"), table.Entity.RowKey);
        Assert.Equal((eventPartitionKey, eventId.ToString("N")), Assert.Single(table.Lookups));

        var loaded = await repository.GetAsync(proposal.OwnerId, eventId, proposal.Id);
        Assert.Equal(eventId, loaded!.EventId);
        Assert.Equal(proposal.OwnerId, loaded.OwnerId);
        Assert.Equal(proposal.SubmissionDate, loaded.SubmissionDate);
        Assert.Equal("2026-03-15", table.Entity.GetString(nameof(SessionProposal.SubmissionDate)));

        var results = await repository.ListByEventAsync(proposal.OwnerId, eventId);
        Assert.Equal(eventId, Assert.Single(results).EventId);
        Assert.StartsWith($"PartitionKey eq '{partitionKey}' and ", Assert.Single(table.Filters));

        Assert.NotNull(await repository.UpdateAsync(proposal));
        Assert.Contains((partitionKey, proposal.Id.ToString("N")), table.Lookups);
        Assert.Equal(partitionKey, table.Entity.PartitionKey);

        Assert.True(await repository.DeleteAsync(proposal.OwnerId, eventId, proposal.Id));
        Assert.Equal((partitionKey, proposal.Id.ToString("N")), table.DeletedKey);
    }

    [Fact]
    public async Task SessionProposalRepository_HandlesMissingAbstract()
    {
        var table = new RecordingTableClient();
        var eventId = Guid.NewGuid();
        await new AzureTableEventRepository(table).CreateAsync(new Event
        {
            Id = eventId,
            OwnerId = "owner-123",
            Name = "Summit",
            Location = "Rome",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 2)
        });
        var repository = new AzureTableSessionProposalRepository(table);
        var proposal = new SessionProposal
        {
            EventId = eventId,
            OwnerId = "owner-123",
            Title = "Storage without abstract"
        };

        await repository.CreateAsync(proposal);

        var loaded = await repository.GetAsync(proposal.OwnerId, eventId, proposal.Id);
        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded.Abstract);
        Assert.Null(loaded.SubmissionDate);
    }

    /// <summary>Verifies cascading deletion targets sessions before deleting the event.</summary>
    [Theory]
    [InlineData("owner-123", "events|owner-123")]
    [InlineData("speaker@example.com", "events|speaker@example.com")]
    [InlineData("speaker#EXT#@example.com", "events|speaker%23EXT%23@example.com")]
    public async Task DeleteEvent_UsesSessionsPartitionForCascade(string ownerId, string eventPartitionKey)
    {
        var eventId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var table = new RecordingTableClient
        {
            Entity = new TableEntity($"sessions|{eventId:N}", proposalId.ToString("N"))
        };

        Assert.True(await new AzureTableEventRepository(table).DeleteAsync(ownerId, eventId));

        Assert.Equal($"PartitionKey eq 'sessions|{eventId:N}' and OwnerId eq '{ownerId}'", Assert.Single(table.Filters));
        var action = Assert.Single(table.TransactionActions);
        Assert.Equal(TableTransactionActionType.Delete, action.ActionType);
        Assert.Equal($"sessions|{eventId:N}", action.Entity.PartitionKey);
        Assert.Equal(proposalId.ToString("N"), action.Entity.RowKey);
        Assert.Equal((eventPartitionKey, eventId.ToString("N")), table.DeletedKey);
    }

    private sealed class RecordingTableClient : TableClient
    {
        public TableEntity? Entity { get; set; }
        public List<(string PartitionKey, string RowKey)> Lookups { get; } = [];
        public List<string?> Filters { get; } = [];
        public List<TableTransactionAction> TransactionActions { get; } = [];
        public (string PartitionKey, string RowKey)? DeletedKey { get; private set; }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            Entity = (TableEntity)(object)entity;
            return Task.FromResult<Response>(null!);
        }

        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
            string partitionKey, string rowKey, IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            Lookups.Add((partitionKey, rowKey));
            return Task.FromResult<NullableResponse<T>>(Response.FromValue((T)(object)Entity!, null!));
        }

        public override AsyncPageable<T> QueryAsync<T>(
            string? filter = null, int? maxPerPage = null, IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            Filters.Add(filter);
            return AsyncPageable<T>.FromPages([Page<T>.FromValues([(T)(object)Entity!], null, null!)]);
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            Entity = (TableEntity)(object)entity;
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
        {
            TransactionActions.AddRange(transactionActions);
            return Task.FromResult(Response.FromValue<IReadOnlyList<Response>>([], null!));
        }

        public override Task<Response> DeleteEntityAsync(
            string partitionKey, string rowKey, ETag ifMatch = default,
            CancellationToken cancellationToken = default)
        {
            DeletedKey = (partitionKey, rowKey);
            return Task.FromResult<Response>(null!);
        }
    }
}