using Azure;
using Azure.Data.Tables;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Storage;

/// <summary>Persists events in Azure Table Storage, partitioned by owner.</summary>
public sealed class AzureTableEventRepository : IEventRepository
{
    private readonly TableClient _table;

    /// <summary>Creates a repository backed by the supplied table service.</summary>
    public AzureTableEventRepository(TableClient tableClient)
    {
        ArgumentNullException.ThrowIfNull(tableClient);
        _table = tableClient;
    }

    /// <inheritdoc />
    public async Task<PagedResult<Event>> SearchAsync(
        string ownerId,
        EventSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(criteria);

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {EventPartitionKey(ownerId)}");
        if (criteria.Year is int year)
        {
            var yearStart = new DateOnly(year, 1, 1).ToString("yyyy-MM-dd");
            var yearEnd = new DateOnly(year, 12, 31).ToString("yyyy-MM-dd");
            filter = $"{filter} and StartDate le '{yearEnd}' and EndDate ge '{yearStart}'";
        }

        var events = new List<Event>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            var value = FromEntity(entity);
            if (Matches(value.Name, criteria.Name) && Matches(value.Location, criteria.Location))
            {
                events.Add(value);
            }
        }

        var page = Math.Max(1, criteria.Page);
        var pageSize = Math.Clamp(criteria.PageSize, 1, 100);
        var ordered = events
            .OrderBy(value => value.StartDate)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id)
            .ToArray();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new PagedResult<Event>(items, page, pageSize, ordered.Length);
    }

    /// <inheritdoc />
    public async Task<Event?> GetAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            EventPartitionKey(ownerId), Key(eventId), cancellationToken: cancellationToken);
        return response.HasValue ? FromEntity(response.Value!) : null;
    }

    /// <inheritdoc />
    public async Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        await _table.AddEntityAsync(ToEntity(value), cancellationToken);
        return value;
    }

    /// <inheritdoc />
    public async Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            EventPartitionKey(value.OwnerId), Key(value.Id), cancellationToken: cancellationToken);
        if (!response.HasValue)
        {
            return null;
        }

        await _table.UpdateEntityAsync(ToEntity(value), response.Value!.ETag, TableUpdateMode.Replace, cancellationToken);
        return value;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            EventPartitionKey(ownerId), Key(eventId), cancellationToken: cancellationToken);
        if (!response.HasValue)
        {
            return false;
        }

        var eventPartition = ProposalPartitionKey(eventId);
        var proposalEntities = new List<TableEntity>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(
            TableClient.CreateQueryFilter(
                $"PartitionKey eq {eventPartition} and OwnerId eq {ownerId}"),
            cancellationToken: cancellationToken))
        {
            proposalEntities.Add(entity);
        }

        foreach (var batch in proposalEntities.Chunk(100))
        {
            var actions = batch.Select(entity => new TableTransactionAction(
                TableTransactionActionType.Delete,
                new TableEntity(entity.PartitionKey, entity.RowKey) { ETag = ETag.All })).ToList();
            await _table.SubmitTransactionAsync(actions, cancellationToken);
        }

        await _table.DeleteEntityAsync(
            EventPartitionKey(ownerId), Key(eventId), response.Value!.ETag, cancellationToken);
        return true;
    }

    internal static string Key(Guid id) => id.ToString("N");

    internal static string EventPartitionKey(string ownerId) => $"events|{ownerId}";

    internal static string ProposalPartitionKey(Guid eventId) => $"sessions|{Key(eventId)}";

    internal static Event FromEntity(TableEntity entity) => new()
    {
        Id = Guid.ParseExact(entity.RowKey, "N"),
        OwnerId = entity.PartitionKey["events|".Length..],
        Name = entity.GetString(nameof(Event.Name))!,
        Location = entity.GetString(nameof(Event.Location))!,
        StartDate = DateOnly.ParseExact(entity.GetString(nameof(Event.StartDate))!, "yyyy-MM-dd"),
        EndDate = DateOnly.ParseExact(entity.GetString(nameof(Event.EndDate))!, "yyyy-MM-dd"),
        Costs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(
            entity.GetString(nameof(Event.Costs))!) ?? new Dictionary<string, decimal>()
    };

    internal static TableEntity ToEntity(Event value) => new(EventPartitionKey(value.OwnerId), Key(value.Id))
    {
        [nameof(Event.Name)] = value.Name,
        [nameof(Event.Location)] = value.Location,
        [nameof(Event.StartDate)] = value.StartDate.ToString("yyyy-MM-dd"),
        [nameof(Event.EndDate)] = value.EndDate.ToString("yyyy-MM-dd"),
        [nameof(Event.Costs)] = System.Text.Json.JsonSerializer.Serialize(value.Costs)
    };

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}