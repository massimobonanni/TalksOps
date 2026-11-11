namespace TalksOps.ApiClient.Contracts;

/// <summary>The speaker-visible feedback configuration, without internal routing data.</summary>
public sealed record FeedbackSettingsDto(
    Guid SessionId, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, string TimeZoneId);

/// <summary>The validity period submitted by the speaker.</summary>
public sealed record SaveFeedbackSettingsRequest(
    DateTimeOffset ValidFrom, DateTimeOffset ValidTo, string TimeZoneId);

/// <summary>The only participant-supplied feedback fields.</summary>
public sealed record SubmitFeedbackRequest(int SessionRating, int SpeakerRating, string? Comment = null);

/// <summary>The event and session titles exposed while feedback is available.</summary>
public sealed record PublicFeedbackDto(string EventTitle, string Title, bool Available);

/// <summary>A feedback entry visible only to the owning speaker.</summary>
public sealed record SessionFeedbackDto(
    Guid Id, Guid SessionId, int SessionRating, int SpeakerRating, string? Comment,
    DateTimeOffset Timestamp, FeedbackMetadataDto Metadata);

/// <summary>Server-acquired request metadata visible only to the owning speaker.</summary>
public sealed record FeedbackMetadataDto(string? IpAddress, string? UserAgent);

/// <summary>The number of feedback entries added to the selected session.</summary>
public sealed record ImportFeedbackResultDto(int FeedbackImported);

/// <summary>Transferable feedback values; a missing or null timestamp uses the server's import time.</summary>
public sealed record ImportFeedbackEntryDto(
    int SessionRating, int SpeakerRating, string? Comment = null, DateTimeOffset? Timestamp = null);
