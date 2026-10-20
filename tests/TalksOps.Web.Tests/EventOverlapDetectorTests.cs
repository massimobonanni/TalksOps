using TalksOps.ApiClient.Contracts;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class EventOverlapDetectorTests
{
    [Fact]
    public void GroupOverlappingEvents_EmptyInputReturnsNoGroups()
    {
        Assert.Empty(EventOverlapDetector.GroupOverlappingEvents([]));
    }

    [Fact]
    public void GroupOverlappingEvents_SortsAndSeparatesDisjointEvents()
    {
        var first = CreateEvent(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2));
        var second = CreateEvent(new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 4));

        var groups = EventOverlapDetector.GroupOverlappingEvents([second, first]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(first, Assert.Single(groups[0]));
        Assert.Equal(second, Assert.Single(groups[1]));
    }

    [Fact]
    public void GroupOverlappingEvents_GroupsIdenticalSingleDayEvents()
    {
        var first = CreateEvent(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1));
        var second = CreateEvent(first.StartDate, first.EndDate);

        var group = Assert.Single(EventOverlapDetector.GroupOverlappingEvents([first, second]));

        Assert.Equal([first, second], group);
    }

    [Fact]
    public void GroupOverlappingEvents_KeepsChainsAndContainedEventsTogether()
    {
        var first = CreateEvent(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10));
        var contained = CreateEvent(new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 3));
        var last = CreateEvent(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12));
        var separate = CreateEvent(new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 14));

        var groups = EventOverlapDetector.GroupOverlappingEvents([last, separate, contained, first]);

        Assert.Equal(2, groups.Count);
        Assert.Equal([first, contained, last], groups[0]);
        Assert.Equal(separate, Assert.Single(groups[1]));
    }

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