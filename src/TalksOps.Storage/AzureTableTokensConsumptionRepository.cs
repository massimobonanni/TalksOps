using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Storage;

/// <summary>Persists model token consumption in its dedicated Azure Table.</summary>
public sealed class AzureTableTokensConsumptionRepository : ITokensConsumptionRepository
{
    private readonly TableClient _table;

    /// <summary>Creates a repository backed by the token-consumption table.</summary>
    public AzureTableTokensConsumptionRepository(
        [FromKeyedServices(AzureTablesStorageExtensions.TokensConsumptionTableClientKey)]
        TableClient tableClient)
    {
        ArgumentNullException.ThrowIfNull(tableClient);
        _table = tableClient;
    }

    /// <inheritdoc />
    public async Task<TokensConsumption> CreateAsync(
        TokensConsumption value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.UserId);

        var normalizedValue = value with { Timestamp = value.Timestamp.ToUniversalTime() };
        await _table.AddEntityAsync(ToEntity(normalizedValue), cancellationToken);
        return normalizedValue;
    }

    internal static TableEntity ToEntity(TokensConsumption value)
    {
        var entity = new TableEntity(
            AzureTableEventRepository.EscapePartitionKey(value.UserId),
            Guid.NewGuid().ToString("N"))
        {
            [nameof(TokensConsumption.DeploymentName)] = value.DeploymentName,
            [nameof(TokensConsumption.Timestamp)] = value.Timestamp,
            [nameof(TokensConsumption.CallForPapersUrl)] = value.CallForPapersUrl
        };

        if (value.InputTokens is int inputTokens)
        {
            entity[nameof(TokensConsumption.InputTokens)] = inputTokens;
        }

        if (value.OutputTokens is int outputTokens)
        {
            entity[nameof(TokensConsumption.OutputTokens)] = outputTokens;
        }

        return entity;
    }
}
