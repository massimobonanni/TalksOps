namespace TalksOps.Core.Domain;

/// <summary>Defines the period and time zone used to accept feedback for a session.</summary>
public sealed record FeedbackSettings
{
    /// <summary>Gets the session identifier.</summary>
    public Guid SessionId { get; init; }

    /// <summary>Gets the internal event reference used for public session lookup.</summary>
    public Guid EventId { get; init; }

    /// <summary>Gets the inclusive start of the feedback period.</summary>
    public DateTimeOffset ValidFrom { get; init; }

    /// <summary>Gets the exclusive end of the feedback period.</summary>
    public DateTimeOffset ValidTo { get; init; }

    /// <summary>Gets the time-zone identifier used to present the validity period.</summary>
    public required string TimeZoneId { get; init; }
}
