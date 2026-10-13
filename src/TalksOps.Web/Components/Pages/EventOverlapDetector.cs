using TalksOps.ApiClient.Contracts;

namespace TalksOps.Web.Components.Pages;

/// <summary>Checks whether a date range overlaps any existing calendar event.</summary>
public static class EventOverlapDetector
{
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