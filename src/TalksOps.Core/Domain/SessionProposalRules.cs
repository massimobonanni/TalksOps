namespace TalksOps.Core.Domain;

/// <summary>Contains validation and lifecycle rules for session proposals.</summary>
public static class SessionProposalRules
{
    /// <summary>Returns validation errors for a proposal, or an empty list when valid.</summary>
    public static IReadOnlyList<string> Validate(SessionProposal value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var errors = new List<string>();
        if (value.EventId == Guid.Empty)
        {
            errors.Add("A parent event is required.");
        }

        if (string.IsNullOrWhiteSpace(value.OwnerId))
        {
            errors.Add("An owner is required.");
        }

        if (string.IsNullOrWhiteSpace(value.Title))
        {
            errors.Add("A proposal title is required.");
        }

        if (string.IsNullOrWhiteSpace(value.Abstract))
        {
            errors.Add("A proposal abstract is required.");
        }

        if (value.UpdatedAt < value.CreatedAt)
        {
            errors.Add("The update timestamp cannot precede the creation timestamp.");
        }

        return errors;
    }

    /// <summary>Determines whether a proposal can move from its current status to a target status.</summary>
    public static bool CanTransition(ProposalStatus current, ProposalStatus target) =>
        Enum.IsDefined(current) && Enum.IsDefined(target) && current != target;
}