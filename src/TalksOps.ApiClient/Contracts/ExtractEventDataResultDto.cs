namespace TalksOps.ApiClient.Contracts;

/// <summary>Event details extracted from a call-for-papers page.</summary>
public sealed record ExtractEventDataResultDto(
    string? Name,
    string? Location,
    string? OfficialWebsiteUrl,
    string? CallForPapersUrl,
    string? CallForPapersDeadline,
    string? StartDate,
    string? EndDate);