namespace TalksOps.Core.Extraction;

/// <summary>Event data extracted from a call-for-papers page.</summary>
public sealed record ExtractedEventData(
    string? Name,
    string? Location,
    string? OfficialWebsiteUrl,
    string? CallForPapersUrl,
    string? CallForPapersDeadline,
    string? StartDate,
    string? EndDate);