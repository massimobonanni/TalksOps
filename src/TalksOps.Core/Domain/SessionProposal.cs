namespace TalksOps.Core.Domain;

/// <summary>Represents a session proposal submitted for an event.</summary>
public sealed record SessionProposal
{
    /// <summary>Gets the stable proposal identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Gets the parent event identifier.</summary>
    public required Guid EventId { get; init; }

    /// <summary>Gets the authenticated event owner's identifier.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Gets the proposal title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the proposal abstract.</summary>
    public required string Abstract { get; init; }

    /// <summary>Gets the speaker's display name.</summary>
    public required string SpeakerName { get; init; }

    /// <summary>Gets the proposal's current status.</summary>
    public ProposalStatus Status { get; init; } = ProposalStatus.Submitted;

    /// <summary>Gets the creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the last update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}