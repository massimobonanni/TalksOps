using TalksOps.Core.Domain;

namespace TalksOps.Core.Contracts;

/// <summary>Defines session proposal application operations for the current user.</summary>
public interface ISessionProposalService
{
    /// <summary>Lists proposals for an event owned by the current user.</summary>
    Task<IReadOnlyList<SessionProposal>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a proposal from an event owned by the current user.</summary>
    Task<SessionProposal?> GetAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a proposal under an event owned by the current user.</summary>
    Task<SessionProposal> CreateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a proposal under an event owned by the current user.</summary>
    Task<SessionProposal?> UpdateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a proposal's status under an event owned by the current user.</summary>
    Task<SessionProposal?> ChangeStatusAsync(
        Guid eventId,
        Guid proposalId,
        ProposalStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a proposal without physically deleting it.</summary>
    Task<SessionProposal?> CancelAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a proposal owned by the current user.</summary>
    Task<bool> DeleteAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);
}