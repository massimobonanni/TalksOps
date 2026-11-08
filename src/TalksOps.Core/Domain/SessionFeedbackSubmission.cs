namespace TalksOps.Core.Domain;

/// <summary>Contains participant-supplied feedback fields, excluding server-owned identifiers and timestamps.</summary>
public sealed record SessionFeedbackSubmission
{
    /// <summary>Gets the session rating from 1 to 10.</summary>
    public int SessionRating { get; init; }

    /// <summary>Gets the speaker rating from 1 to 10.</summary>
    public int SpeakerRating { get; init; }

    /// <summary>Gets the optional comment or suggestion.</summary>
    public string? Comment { get; init; }
}
