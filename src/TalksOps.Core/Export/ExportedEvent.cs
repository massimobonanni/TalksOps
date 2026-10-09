namespace TalksOps.Core.Export;

/// <summary>JSON representation of an exported event and its sessions.</summary>
/// <remarks>The owner is intentionally omitted: on import, events are always assigned to the current user.</remarks>
public sealed record ExportedEvent
{
    /// <summary>Gets the identifier the event had in the source system.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the event name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the event location.</summary>
    public required string Location { get; init; }

    /// <summary>Gets the official event website URL.</summary>
    public string? OfficialWebsiteUrl { get; init; }

    /// <summary>Gets the call for papers URL.</summary>
    public string? CallForPapersUrl { get; init; }

    /// <summary>Gets the event start date.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Gets the event end date.</summary>
    public required DateOnly EndDate { get; init; }

    /// <summary>Gets named event costs.</summary>
    public IReadOnlyDictionary<string, decimal> Costs { get; init; } = new Dictionary<string, decimal>();

    /// <summary>Gets the sessions proposed for the event.</summary>
    public IReadOnlyList<ExportedSession> Sessions { get; init; } = [];
}
