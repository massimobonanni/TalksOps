using TalksOps.Core.Domain;

namespace TalksOps.Core.Tests;

/// <summary>Verifies feedback settings and submission validation.</summary>
public sealed class FeedbackRulesTests
{
    [Fact]
    public void Validate_AcceptsValidSettingsAndRatingBounds()
    {
        var settings = new FeedbackSettings
        {
            SessionId = Guid.NewGuid(),
            ValidFrom = DateTimeOffset.Parse("2026-10-23T09:00:00Z"),
            ValidTo = DateTimeOffset.Parse("2026-10-23T10:00:00Z"),
            TimeZoneId = "Europe/Rome"
        };

        Assert.Empty(FeedbackRules.Validate(settings));
        Assert.Empty(FeedbackRules.ValidateSubmission(new SessionFeedbackSubmission
        {
            SessionRating = 1,
            SpeakerRating = 10
        }));
    }

    [Fact]
    public void Validate_RejectsInvalidSettingsAndRatings()
    {
        var settings = new FeedbackSettings
        {
            SessionId = Guid.Empty,
            ValidFrom = DateTimeOffset.Parse("2026-10-23T10:00:00Z"),
            ValidTo = DateTimeOffset.Parse("2026-10-23T09:00:00Z"),
            TimeZoneId = " "
        };

        var settingsErrors = FeedbackRules.Validate(settings);
        var submissionErrors = FeedbackRules.ValidateSubmission(new SessionFeedbackSubmission
        {
            SessionRating = 0,
            SpeakerRating = 11
        });

        Assert.Equal(3, settingsErrors.Count);
        Assert.Equal(2, submissionErrors.Count);
    }
}
