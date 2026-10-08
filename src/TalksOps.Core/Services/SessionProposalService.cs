using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Core.Services;

/// <summary>Implements proposal operations scoped to the current user.</summary>
public sealed class SessionProposalService(
    ISessionProposalRepository repository,
    ICurrentUserContext currentUser) : ISessionProposalService
{
    private readonly ISessionProposalRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ICurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionProposal>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default) =>
        this.repository.ListByEventAsync(this.currentUser.UserId, eventId, cancellationToken);

    /// <inheritdoc />
    public Task<SessionProposal?> GetAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default) =>
        this.repository.GetAsync(this.currentUser.UserId, eventId, proposalId, cancellationToken);

    /// <inheritdoc />
    public Task<SessionProposal> CreateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var owned = proposal with { OwnerId = this.currentUser.UserId };
        Validate(owned);
        return this.repository.CreateAsync(owned, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SessionProposal?> UpdateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var existing = await this.GetAsync(proposal.EventId, proposal.Id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Title = proposal.Title,
            Abstract = proposal.Abstract,
            Notes = proposal.Notes,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Validate(updated);
        return await this.repository.UpdateAsync(updated, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SessionProposal?> ChangeStatusAsync(
        Guid eventId,
        Guid proposalId,
        ProposalStatus status,
        CancellationToken cancellationToken = default)
    {
        var existing = await this.GetAsync(eventId, proposalId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.Status == status)
        {
            return existing;
        }

        if (!SessionProposalRules.CanTransition(existing.Status, status))
        {
            throw new InvalidOperationException($"A proposal cannot transition from {existing.Status} to {status}.");
        }

        return await this.repository.UpdateAsync(
            existing with { Status = status, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SessionProposal?> CancelAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default) =>
        this.ChangeStatusAsync(eventId, proposalId, ProposalStatus.Cancelled, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default) =>
        this.repository.DeleteAsync(this.currentUser.UserId, eventId, proposalId, cancellationToken);

    private static void Validate(SessionProposal value)
    {
        var errors = SessionProposalRules.Validate(value);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }
    }
}