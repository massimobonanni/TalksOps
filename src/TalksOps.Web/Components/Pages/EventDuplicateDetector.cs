using TalksOps.ApiClient.Contracts;

namespace TalksOps.Web.Components.Pages;

/// <summary>Checks whether an event matches an existing event by title or call-for-papers details.</summary>
public static class EventDuplicateDetector
{
    /// <summary>Returns whether any event has the same title, excluding an optional event identifier.</summary>
    public static bool HasSameTitle(
        IEnumerable<EventDto> events,
        string title,
        Guid? excludedEventId = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(title);

        var normalizedTitle = title.Trim();
        return normalizedTitle.Length > 0 && events.Any(eventItem =>
            eventItem.Id != excludedEventId
            && string.Equals(eventItem.Name.Trim(), normalizedTitle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns whether any event has the same call-for-papers URL and exact date interval.</summary>
    public static bool HasSameCallForPapersAndDates(
        IEnumerable<CalendarEventDto> events,
        string? callForPapersUrl,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludedEventId = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var normalizedUrl = callForPapersUrl?.Trim();
        return !string.IsNullOrEmpty(normalizedUrl) && events.Any(eventItem =>
            eventItem.EventId != excludedEventId
            && eventItem.StartDate == startDate
            && eventItem.EndDate == endDate
            && string.Equals(eventItem.CallForPapersUrl?.Trim(), normalizedUrl, StringComparison.OrdinalIgnoreCase));
    }
}