namespace TalksOps.Core.Domain;

/// <summary>Represents an event owned by a TalksOps user.</summary>
public sealed record Event
{
    /// <summary>Gets the stable event identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Gets the authenticated owner's identifier.</summary>
    public required string OwnerId { get; init; }

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

    /// <summary>Gets named event costs, expressed in a single unspecified currency.</summary>
    public IReadOnlyDictionary<string, decimal> Costs { get; init; } =
        new Dictionary<string, decimal>();
}