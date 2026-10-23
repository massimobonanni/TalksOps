using Azure;
using Azure.Data.Tables;
using TalksOps.Core.Domain;
using TalksOps.Storage;

namespace TalksOps.Storage.Tests;

/// <summary>Verifies token-consumption entities are persisted with safe, unique table keys.</summary>
public sealed class AzureTableTokensConsumptionRepositoryTests
{
    [Theory]
    [InlineData("person/name#?@example.com", "person%2Fname%23%3F@example.com")]
    [InlineData("person\\name", "person%5Cname")]
    [InlineData("person%23@example.com", "person%2523@example.com")]
    [InlineData("person\u0001@example.com", "person%01@example.com")]
    public async Task CreateAsync_MapsConsumptionAndEscapesUserId(
        string userId,
        string partitionKey)
    {
        var table = new RecordingTableClient();
        var repository = new AzureTableTokensConsumptionRepository(table);
        var timestamp = new DateTimeOffset(2026, 10, 23, 17, 12, 0, TimeSpan.FromHours(-2));
        var value = new TokensConsumption
        {
            UserId = userId,
            Timestamp = timestamp,
            DeploymentName = "gpt-deployment",
            InputTokens = 123,
            OutputTokens = 45,
            CallForPapersUrl = "https://example.com/cfp"
        };

        var created = await repository.CreateAsync(value);
        var entity = Assert.Single(table.Entities);

        Assert.Equal(value with { Timestamp = timestamp.ToUniversalTime() }, created);
        Assert.Equal(partitionKey, entity.PartitionKey);
        Assert.True(Guid.TryParseExact(entity.RowKey, "N", out _));
        Assert.Equal("gpt-deployment", entity.GetString(nameof(TokensConsumption.DeploymentName)));
        Assert.Equal(timestamp.ToUniversalTime(), entity.GetDateTimeOffset(nameof(TokensConsumption.Timestamp)));
        Assert.Equal(123, entity.GetInt32(nameof(TokensConsumption.InputTokens)));
        Assert.Equal(45, entity.GetInt32(nameof(TokensConsumption.OutputTokens)));
        Assert.Equal("https://example.com/cfp", entity.GetString(nameof(TokensConsumption.CallForPapersUrl)));
    }

    [Fact]
    public async Task CreateAsync_OmitsUnreportedTokenCountsAndUsesUniqueRowKeys()
    {
        var table = new RecordingTableClient();
        var repository = new AzureTableTokensConsumptionRepository(table);
        var value = new TokensConsumption
        {
            UserId = "person@example.com",
            DeploymentName = "gpt-deployment",
            CallForPapersUrl = "https://example.com/cfp"
        };

        await repository.CreateAsync(value);
        await repository.CreateAsync(value);

        Assert.Equal(2, table.Entities.Count);
        Assert.NotEqual(table.Entities[0].RowKey, table.Entities[1].RowKey);
        Assert.Null(table.Entities[0].GetInt32(nameof(TokensConsumption.InputTokens)));
        Assert.Null(table.Entities[0].GetInt32(nameof(TokensConsumption.OutputTokens)));
    }

    private sealed class RecordingTableClient : TableClient
    {
        public List<TableEntity> Entities { get; } = [];

        public override Task<Response> AddEntityAsync<T>(
            T entity,
            CancellationToken cancellationToken = default)
        {
            Entities.Add((TableEntity)(object)entity);
            return Task.FromResult<Response>(null!);
        }
    }
}
