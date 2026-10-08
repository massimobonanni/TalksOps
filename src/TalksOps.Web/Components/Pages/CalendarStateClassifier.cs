using TalksOps.Core.Domain;

namespace TalksOps.Web.Components.Pages;

/// <summary>Classifies an event's calendar state using proposal status precedence.</summary>
public static class CalendarStateClassifier
{
    /// <summary>Returns Accepted, Submitted, Rejected, Cancelled, or none in that precedence order.</summary>
    public static string GetState(IReadOnlyCollection<string> proposalStatuses)
    {
        ArgumentNullException.ThrowIfNull(proposalStatuses);

        if (proposalStatuses.Contains(nameof(ProposalStatus.Accepted), StringComparer.OrdinalIgnoreCase))
        {
            return "accepted";
        }

        if (proposalStatuses.Contains(nameof(ProposalStatus.Submitted), StringComparer.OrdinalIgnoreCase))
        {
            return "submitted";
        }

        if (proposalStatuses.Contains(nameof(ProposalStatus.Rejected), StringComparer.OrdinalIgnoreCase))
        {
            return "rejected";
        }

        return proposalStatuses.Contains(nameof(ProposalStatus.Cancelled), StringComparer.OrdinalIgnoreCase)
            ? "cancelled"
            : "none";
    }
}