using TalksOps.ApiClient.Contracts;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class EventOverlapDetectorTests
{
    [Fact]
    public void HasOverlap_ReturnsFalseForDisjointRanges()
    {
        var events = new[] { CreateEvent(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)) };

        Assert.False(EventOverlapDetector.HasOverlap(events, new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 15)));
    }

    [Theory]
    [InlineData(8, 14)]
    [InlineData(10, 11)]
    public void HasOverlap_ReturnsTrueForIntersectingRanges(int startDay, int endDay)
    {
        var events = new[] { CreateEvent(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)) };

        Assert.True(EventOverlapDetector.HasOverlap(events, new DateOnly(2026, 6, startDay), new DateOnly(2026, 6, endDay)));
    }

    [Fact]
    public void HasOverlap_ReturnsTrueWhenRangesShareOneDay()
    {
        var events = new[] { CreateEvent(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)) };

        Assert.True(EventOverlapDetector.HasOverlap(events, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 14)));
    }

    [Fact]
    public void HasOverlap_IgnoresTheEventBeingEdited()
    {
        var eventItem = CreateEvent(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12));

        Assert.False(EventOverlapDetector.HasOverlap([eventItem], eventItem.StartDate, eventItem.EndDate, eventItem.EventId));
    }

    [Fact]
    public void HasOverlap_ReturnsTrueWhenMultipleEventsOverlap()
    {
        var events = new[]
        {
            CreateEvent(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)),
            CreateEvent(new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 15))
        };

        Assert.True(EventOverlapDetector.HasOverlap(events, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 13)));
    }

    private static CalendarEventDto CreateEvent(DateOnly startDate, DateOnly endDate) =>
        new(Guid.NewGuid(), "Event", startDate, endDate, []);
}