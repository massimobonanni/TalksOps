namespace TalksOps.Core.Domain;

/// <summary>Contains transferable feedback values supplied by an import document.</summary>
public sealed record SessionFeedbackImport
{
    /// <summary>Gets the session rating from 1 to 10.</summary>
    public int SessionRating { get; init; }

    /// <summary>Gets the speaker rating from 1 to 10.</summary>
    public int SpeakerRating { get; init; }

    /// <summary>Gets the optional comment or suggestion.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets the original UTC submission timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
