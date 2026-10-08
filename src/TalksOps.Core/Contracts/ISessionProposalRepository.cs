using TalksOps.Core.Domain;

namespace TalksOps.Core.Contracts;

/// <summary>Defines persistence operations for session proposals.</summary>
public interface ISessionProposalRepository
{
    /// <summary>Lists proposals for an event owned by the specified user.</summary>
    Task<IReadOnlyList<SessionProposal>> ListByEventAsync(
        string ownerId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a proposal under an event owned by the specified user.</summary>
    Task<SessionProposal?> GetAsync(
        string ownerId,
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a proposal.</summary>
    Task<SessionProposal> CreateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing proposal.</summary>
    Task<SessionProposal?> UpdateAsync(
        SessionProposal proposal,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a proposal owned by the specified user.</summary>
    Task<bool> DeleteAsync(
        string ownerId,
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);
}