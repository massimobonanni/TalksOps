using TalksOps.ApiClient.Contracts;

namespace TalksOps.Web.Components.Pages;

/// <summary>Checks whether a date range overlaps any existing calendar event.</summary>
public static class EventOverlapDetector
{
    /// <summary>Groups chronologically ordered events connected by overlapping date ranges, including shared boundary days.</summary>
    public static IReadOnlyList<IReadOnlyList<CalendarEventDto>> GroupOverlappingEvents(
        IReadOnlyCollection<CalendarEventDto> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var groups = new List<IReadOnlyList<CalendarEventDto>>();
        List<CalendarEventDto>? currentGroup = null;
        var groupEndDate = DateOnly.MinValue;
        foreach (var eventItem in events.OrderBy(eventItem => eventItem.StartDate))
        {
            if (currentGroup is null || eventItem.StartDate > groupEndDate)
            {
                currentGroup = [];
                groups.Add(currentGroup);
                groupEndDate = eventItem.EndDate;
            }

            currentGroup.Add(eventItem);
            if (eventItem.EndDate > groupEndDate)
            {
                groupEndDate = eventItem.EndDate;
            }
        }

        return groups;
    }

    /// <summary>Returns whether the date range overlaps an event other than the optional excluded event.</summary>
    public static bool HasOverlap(
        IReadOnlyCollection<CalendarEventDto> events,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludedEventId = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events.Any(eventItem => eventItem.EventId != excludedEventId
            && startDate <= eventItem.EndDate
            && endDate >= eventItem.StartDate);
    }
}