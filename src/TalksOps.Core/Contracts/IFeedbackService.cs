using TalksOps.Core.Domain;

namespace TalksOps.Core.Contracts;

/// <summary>Defines persistence operations for feedback settings and session feedback.</summary>
public interface IFeedbackService
{
    /// <summary>Resolves a configured session's title by session ID without a current-user context.</summary>
    Task<string?> GetSessionTitleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets feedback settings for a session, or null when none are configured.</summary>
    Task<FeedbackSettings?> GetSettingsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces feedback settings for a session.</summary>
    Task<FeedbackSettings> SaveSettingsAsync(
        FeedbackSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a session's feedback from newest to oldest.</summary>
    Task<IReadOnlyList<SessionFeedback>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates feedback using a server-generated identifier and timestamp.</summary>
    Task<SessionFeedback> CreateAsync(
        Guid sessionId,
        SessionFeedbackSubmission submission,
        SessionFeedbackMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>Imports feedback and optional settings for a newly created session.</summary>
    Task ImportSessionAsync(
        Guid sessionId,
        FeedbackSettings? settings,
        IReadOnlyList<SessionFeedbackImport> feedback,
        CancellationToken cancellationToken = default);
}
