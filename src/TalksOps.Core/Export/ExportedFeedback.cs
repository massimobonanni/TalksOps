namespace TalksOps.Core.Export;

/// <summary>Transferable feedback values without identifiers or request metadata.</summary>
public sealed record ExportedFeedback
{
    /// <summary>Gets the session rating from 1 to 10.</summary>
    public int SessionRating { get; init; }

    /// <summary>Gets the speaker rating from 1 to 10.</summary>
    public int SpeakerRating { get; init; }

    /// <summary>Gets the optional comment or suggestion.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets the original submission timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
