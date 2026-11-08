namespace TalksOps.Core.Export;

/// <summary>Transferable feedback validity settings without internal session or event identifiers.</summary>
public sealed record ExportedFeedbackSettings
{
    /// <summary>Gets the inclusive start of the feedback period.</summary>
    public DateTimeOffset ValidFrom { get; init; }

    /// <summary>Gets the exclusive end of the feedback period.</summary>
    public DateTimeOffset ValidTo { get; init; }

    /// <summary>Gets the time-zone identifier used to present the validity period.</summary>
    public required string TimeZoneId { get; init; }
}
