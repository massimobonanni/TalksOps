using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Core.Services;

/// <summary>Implements event operations scoped to the current user.</summary>
public sealed class EventService(IEventRepository repository, ICurrentUserContext currentUser) : IEventService
{
    private readonly IEventRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ICurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public Task<PagedResult<Event>> SearchAsync(EventSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return this.repository.SearchAsync(this.currentUser.UserId, criteria, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Event?> GetAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        this.repository.GetAsync(this.currentUser.UserId, eventId, cancellationToken);

    /// <inheritdoc />
    public Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var owned = value with { OwnerId = this.currentUser.UserId };
        Validate(owned);
        return this.repository.CreateAsync(owned, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var owned = value with { OwnerId = this.currentUser.UserId };
        Validate(owned);
        return this.repository.UpdateAsync(owned, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        this.repository.DeleteAsync(this.currentUser.UserId, eventId, cancellationToken);

    private static void Validate(Event value)
    {
        var errors = EventRules.Validate(value);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }
    }
}