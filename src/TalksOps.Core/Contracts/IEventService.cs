using TalksOps.Core.Domain;

namespace TalksOps.Core.Contracts;

/// <summary>Defines event application operations for the current user.</summary>
public interface IEventService
{
    /// <summary>Searches the current user's events.</summary>
    Task<PagedResult<Event>> SearchAsync(
        EventSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an event owned by the current user.</summary>
    Task<Event?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Creates an event for the current user.</summary>
    Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default);

    /// <summary>Updates an event owned by the current user.</summary>
    Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default);

    /// <summary>Deletes an event owned by the current user.</summary>
    Task<bool> DeleteAsync(Guid eventId, CancellationToken cancellationToken = default);
}