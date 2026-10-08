using Azure.Data.Tables;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Storage;

/// <summary>Persists session proposals in Azure Table Storage, partitioned by event.</summary>
public sealed class AzureTableSessionProposalRepository : ISessionProposalRepository
{
    private readonly TableClient _table;

    /// <summary>Creates a proposal repository backed by the supplied table service.</summary>
    public AzureTableSessionProposalRepository(TableClient tableClient)
    {
        ArgumentNullException.ThrowIfNull(tableClient);
        _table = tableClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionProposal>> ListByEventAsync(
        string ownerId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!await OwnsEventAsync(ownerId, eventId, cancellationToken))
        {
            return [];
        }

        var results = new List<SessionProposal>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(
            TableClient.CreateQueryFilter(
                $"PartitionKey eq {AzureTableEventRepository.ProposalPartitionKey(eventId)} and OwnerId eq {ownerId}"),
            cancellationToken: cancellationToken))
        {
            results.Add(FromEntity(entity));
        }

        return results.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id).ToArray();
    }

    /// <inheritdoc />
    public async Task<SessionProposal?> GetAsync(
        string ownerId,
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!await OwnsEventAsync(ownerId, eventId, cancellationToken))
        {
            return null;
        }

        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            AzureTableEventRepository.ProposalPartitionKey(eventId),
            AzureTableEventRepository.Key(proposalId),
            cancellationToken: cancellationToken);
        return response.HasValue && string.Equals(
            response.Value!.GetString(nameof(SessionProposal.OwnerId)), ownerId, StringComparison.Ordinal)
            ? FromEntity(response.Value)
            : null;
    }

    /// <inheritdoc />
    public async Task<SessionProposal> CreateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!await OwnsEventAsync(proposal.OwnerId, proposal.EventId, cancellationToken))
        {
            throw new InvalidOperationException("The proposal owner does not own the specified event.");
        }

        await _table.AddEntityAsync(ToEntity(proposal), cancellationToken);
        return proposal;
    }

    /// <inheritdoc />
    public async Task<SessionProposal?> UpdateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!await OwnsEventAsync(proposal.OwnerId, proposal.EventId, cancellationToken))
        {
            return null;
        }

        var partitionKey = AzureTableEventRepository.ProposalPartitionKey(proposal.EventId);
        var rowKey = AzureTableEventRepository.Key(proposal.Id);
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            partitionKey, rowKey, cancellationToken: cancellationToken);
        if (!response.HasValue || !string.Equals(
            response.Value!.GetString(nameof(SessionProposal.OwnerId)), proposal.OwnerId, StringComparison.Ordinal))
        {
            return null;
        }

        await _table.UpdateEntityAsync(ToEntity(proposal), response.Value.ETag, TableUpdateMode.Replace, cancellationToken);
        return proposal;
    }

    private async Task<bool> OwnsEventAsync(string ownerId, Guid eventId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            AzureTableEventRepository.EventPartitionKey(ownerId),
            AzureTableEventRepository.Key(eventId),
            cancellationToken: cancellationToken);
        return response.HasValue;
    }

    private static SessionProposal FromEntity(TableEntity entity) => new()
    {
        Id = Guid.ParseExact(entity.RowKey, "N"),
        EventId = Guid.ParseExact(entity.PartitionKey["sessions|".Length..], "N"),
        OwnerId = entity.GetString(nameof(SessionProposal.OwnerId))!,
        Title = entity.GetString(nameof(SessionProposal.Title))!,
        Abstract = entity.GetString(nameof(SessionProposal.Abstract))!,
        SpeakerName = entity.GetString(nameof(SessionProposal.SpeakerName))!,
        Status = Enum.Parse<ProposalStatus>(entity.GetString(nameof(SessionProposal.Status))!),
        CreatedAt = entity.GetDateTimeOffset(nameof(SessionProposal.CreatedAt))!.Value,
        UpdatedAt = entity.GetDateTimeOffset(nameof(SessionProposal.UpdatedAt))!.Value
    };

    private static TableEntity ToEntity(SessionProposal value) => new(
        AzureTableEventRepository.ProposalPartitionKey(value.EventId), AzureTableEventRepository.Key(value.Id))
    {
        [nameof(SessionProposal.OwnerId)] = value.OwnerId,
        [nameof(SessionProposal.Title)] = value.Title,
        [nameof(SessionProposal.Abstract)] = value.Abstract,
        [nameof(SessionProposal.SpeakerName)] = value.SpeakerName,
        [nameof(SessionProposal.Status)] = value.Status.ToString(),
        [nameof(SessionProposal.CreatedAt)] = value.CreatedAt,
        [nameof(SessionProposal.UpdatedAt)] = value.UpdatedAt
    };
}