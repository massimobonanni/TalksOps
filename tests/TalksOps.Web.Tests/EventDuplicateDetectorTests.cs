using TalksOps.ApiClient.Contracts;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class EventDuplicateDetectorTests
{
    [Fact]
    public void HasSameTitle_MatchesTrimmedTitleRegardlessOfDatesAndCase()
    {
        var events = new[] { CreateEvent("Tech Summit", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2)) };

        Assert.True(EventDuplicateDetector.HasSameTitle(events, " tech summit "));
    }

    [Fact]
    public void HasSameTitle_IgnoresTheEventBeingEdited()
    {
        var eventItem = CreateEvent("Tech Summit", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        Assert.False(EventDuplicateDetector.HasSameTitle([eventItem], eventItem.Name, eventItem.Id));
    }

    [Fact]
    public void HasSameCallForPapersAndDates_RequiresSameUrlAndExactInterval()
    {
        var eventItem = new CalendarEventDto(
            Guid.NewGuid(), "Conference", new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12), [],
            CallForPapersUrl: "https://example.test/cfp");

        Assert.True(EventDuplicateDetector.HasSameCallForPapersAndDates(
            [eventItem], " https://example.test/cfp ", eventItem.StartDate, eventItem.EndDate));
        Assert.False(EventDuplicateDetector.HasSameCallForPapersAndDates(
            [eventItem], eventItem.CallForPapersUrl, eventItem.StartDate, eventItem.EndDate.AddDays(1)));
        Assert.False(EventDuplicateDetector.HasSameCallForPapersAndDates(
            [eventItem], "https://example.test/other", eventItem.StartDate, eventItem.EndDate));
    }

    private static EventDto CreateEvent(string name, DateOnly startDate, DateOnly endDate) =>
        new(Guid.NewGuid(), "owner", name, "location", startDate, endDate, new Dictionary<string, decimal>());
}