namespace TalksOps.ApiClient.Contracts;

/// <summary>Event summary returned by the annual calendar endpoint.</summary>
public sealed record CalendarEventDto(
    Guid EventId,
    string EventName,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<string> ProposalStatuses);