namespace TalksOps.ApiClient.Contracts;

/// <summary>REST representation of an event.</summary>
public sealed record EventDto(
    Guid Id,
    string OwnerId,
    string Name,
    string Location,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyDictionary<string, decimal> Costs);