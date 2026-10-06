using TalksOps.Core.Domain;

namespace TalksOps.Core.Contracts;

/// <summary>Defines persistence operations for events, scoped to an owner.</summary>
public interface IEventRepository
{
    /// <summary>Searches events belonging to the specified owner.</summary>
    Task<PagedResult<Event>> SearchAsync(
        string ownerId,
        EventSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an event belonging to the specified owner.</summary>
    Task<Event?> GetAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Creates an event.</summary>
    Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default);

    /// <summary>Updates an event belonging to the specified owner.</summary>
    Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default);

    /// <summary>Deletes an event belonging to the specified owner and its proposals.</summary>
    Task<bool> DeleteAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default);
}