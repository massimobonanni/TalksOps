using TalksOps.Core.Domain;

namespace TalksOps.Core.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void EventRules_RejectsEndDateBeforeStartDate()
    {
        var value = new Event
        {
            OwnerId = "user-1",
            Name = "Tech conference",
            Location = "Milan",
            StartDate = new DateOnly(2026, 5, 2),
            EndDate = new DateOnly(2026, 5, 1)
        };

        Assert.Contains(EventRules.Validate(value), error => error.Contains("end date"));
    }

    [Fact]
    public void EventRules_RejectsNegativeCosts()
    {
        var value = new Event
        {
            OwnerId = "user-1",
            Name = "Tech conference",
            Location = "Milan",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 2),
            Costs = new Dictionary<string, decimal> { ["Travel"] = -1m }
        };

        Assert.Contains(EventRules.Validate(value), error => error.Contains("negative"));
    }

    [Fact]
    public void SessionProposalRules_AllowsMissingNotes()
    {
        var value = new SessionProposal
        {
            EventId = Guid.NewGuid(),
            OwnerId = "user-1",
            Title = "Reliable systems",
            Abstract = "A proposal abstract"
        };

        Assert.Empty(SessionProposalRules.Validate(value));
    }

    [Theory]
    [InlineData(ProposalStatus.Submitted, ProposalStatus.Accepted, true)]
    [InlineData(ProposalStatus.Submitted, ProposalStatus.Rejected, true)]
    [InlineData(ProposalStatus.Submitted, ProposalStatus.Cancelled, true)]
    [InlineData(ProposalStatus.Accepted, ProposalStatus.Submitted, true)]
    [InlineData(ProposalStatus.Accepted, ProposalStatus.Rejected, true)]
    [InlineData(ProposalStatus.Accepted, ProposalStatus.Cancelled, true)]
    [InlineData(ProposalStatus.Rejected, ProposalStatus.Submitted, true)]
    [InlineData(ProposalStatus.Rejected, ProposalStatus.Accepted, true)]
    [InlineData(ProposalStatus.Rejected, ProposalStatus.Cancelled, true)]
    [InlineData(ProposalStatus.Cancelled, ProposalStatus.Submitted, true)]
    [InlineData(ProposalStatus.Cancelled, ProposalStatus.Accepted, true)]
    [InlineData(ProposalStatus.Cancelled, ProposalStatus.Rejected, true)]
    [InlineData(ProposalStatus.Accepted, ProposalStatus.Accepted, false)]
    public void SessionProposalRules_EnforcesStatusTransitions(
        ProposalStatus current,
        ProposalStatus target,
        bool expected)
    {
        Assert.Equal(expected, SessionProposalRules.CanTransition(current, target));
    }
}