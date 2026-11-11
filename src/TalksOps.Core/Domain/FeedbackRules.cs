namespace TalksOps.Core.Domain;

/// <summary>Contains validation rules for feedback settings and submissions.</summary>
public static class FeedbackRules
{
    /// <summary>Returns validation errors for feedback settings.</summary>
    public static IReadOnlyList<string> Validate(FeedbackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();
        if (settings.SessionId == Guid.Empty)
        {
            errors.Add("A session is required.");
        }

        if (settings.ValidFrom >= settings.ValidTo)
        {
            errors.Add("The feedback start must precede its end.");
        }

        if (string.IsNullOrWhiteSpace(settings.TimeZoneId))
        {
            errors.Add("A time-zone identifier is required.");
        }

        return errors;
    }

    /// <summary>Returns validation errors for participant-supplied feedback.</summary>
    public static IReadOnlyList<string> ValidateSubmission(SessionFeedbackSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var errors = new List<string>();
        if (submission.SessionRating is < 1 or > 10)
        {
            errors.Add("The session rating must be between 1 and 10.");
        }

        if (submission.SpeakerRating is < 1 or > 10)
        {
            errors.Add("The speaker rating must be between 1 and 10.");
        }

        if (submission.Comment is { Length: > 4000 })
        {
            errors.Add("The feedback comment must not exceed 4000 characters.");
        }

        return errors;
    }

    /// <summary>Returns validation errors for feedback imported with its original timestamp.</summary>
    public static IReadOnlyList<string> ValidateImportedFeedback(SessionFeedbackImport feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        var errors = ValidateSubmission(new SessionFeedbackSubmission
        {
            SessionRating = feedback.SessionRating,
            SpeakerRating = feedback.SpeakerRating,
            Comment = feedback.Comment
        }).ToList();
        if (feedback.Timestamp == default)
        {
            errors.Add("A feedback timestamp is required.");
        }

        return errors;
    }
}
