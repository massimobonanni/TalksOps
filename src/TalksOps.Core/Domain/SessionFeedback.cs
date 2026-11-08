namespace TalksOps.Core.Domain;

/// <summary>Represents one anonymous rating submitted for a session.</summary>
public sealed record SessionFeedback
{
    /// <summary>Gets the feedback identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the session identifier.</summary>
    public Guid SessionId { get; init; }

    /// <summary>Gets the session rating from 1 to 10.</summary>
    public int SessionRating { get; init; }

    /// <summary>Gets the speaker rating from 1 to 10.</summary>
    public int SpeakerRating { get; init; }

    /// <summary>Gets the optional comment or suggestion.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets the server-generated UTC submission timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets request metadata captured by the server.</summary>
    public required SessionFeedbackMetadata Metadata { get; init; }
}
