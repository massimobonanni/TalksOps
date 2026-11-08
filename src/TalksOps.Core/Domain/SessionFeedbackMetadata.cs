namespace TalksOps.Core.Domain;

/// <summary>Contains request metadata stored with a feedback submission.</summary>
public sealed record SessionFeedbackMetadata
{
    /// <summary>Gets the client IP address, when available.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Gets the request user-agent, when available.</summary>
    public string? UserAgent { get; init; }
}
