namespace TalksOps.ApiClient.Contracts;

/// <summary>Fields accepted when creating or updating an event.</summary>
public sealed record SaveEventRequest(
    string Name,
    string Location,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyDictionary<string, decimal> Costs);