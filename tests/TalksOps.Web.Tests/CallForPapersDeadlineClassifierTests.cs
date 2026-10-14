using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class CallForPapersDeadlineClassifierTests
{
    [Theory]
    [InlineData(-1, "deadline-expired", "Expired")]
    [InlineData(0, "deadline-urgent", "0 days")]
    [InlineData(1, "deadline-soon", "1 day")]
    [InlineData(6, "deadline-soon", "6 days")]
    [InlineData(7, "deadline-upcoming", "7 days")]
    public void Classify_UsesRemainingDaysForColorAndLabel(int daysRemaining, string expectedClass, string expectedLabel)
    {
        var today = new DateOnly(2026, 10, 14);

        var result = CallForPapersDeadlineClassifier.Classify(today.AddDays(daysRemaining), today);

        Assert.Equal(expectedClass, result.ColorClass);
        Assert.Equal(expectedLabel, result.RemainingLabel);
    }
}