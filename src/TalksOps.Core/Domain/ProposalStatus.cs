namespace TalksOps.Core.Domain;

/// <summary>Represents the lifecycle state of a session proposal.</summary>
public enum ProposalStatus
{
    Submitted,
    Accepted,
    Rejected,
    Cancelled
}