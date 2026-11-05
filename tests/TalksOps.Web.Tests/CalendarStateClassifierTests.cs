using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class CalendarStateClassifierTests
{
    [Theory]
    [InlineData("Accepted", "accepted")]
    [InlineData("Submitted", "submitted")]
    [InlineData("Rejected", "rejected")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("unknown", "unknown")]
    public void GetProposalState_MapsProposalStatusToIndicatorClass(string status, string expected)
    {
        Assert.Equal(expected, CalendarStateClassifier.GetProposalState(status));
    }

    [Theory]
    [InlineData("Accepted", "accepted")]
    [InlineData("Submitted", "submitted")]
    [InlineData("Rejected", "rejected")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("unknown", "none")]
    public void GetState_ClassifiesSingleProposalStatus(string status, string expected)
    {
        Assert.Equal(expected, CalendarStateClassifier.GetState([status]));
    }

    [Fact]
    public void GetState_UsesDocumentedPrecedenceForMixedStatuses()
    {
        Assert.Equal("accepted", CalendarStateClassifier.GetState(["Cancelled", "Rejected", "Submitted", "Accepted"]));
        Assert.Equal("submitted", CalendarStateClassifier.GetState(["Cancelled", "Rejected", "Submitted"]));
        Assert.Equal("rejected", CalendarStateClassifier.GetState(["Cancelled", "Rejected"]));
    }

    [Fact]
    public void GetState_IsCaseInsensitiveAndReturnsNoneWithoutStatuses()
    {
        Assert.Equal("accepted", CalendarStateClassifier.GetState(["aCcEpTeD"]));
        Assert.Equal("none", CalendarStateClassifier.GetState([]));
    }

    [Fact]
    public void MatchesStatusFilter_ShowsEveryEventWhenNoStatusesAreSelected()
    {
        Assert.True(CalendarStateClassifier.MatchesStatusFilter([], []));
        Assert.True(CalendarStateClassifier.MatchesStatusFilter(["Accepted"], []));
    }

    [Fact]
    public void MatchesStatusFilter_MatchesAnySelectedStatusWithoutCaseSensitivity()
    {
        Assert.True(CalendarStateClassifier.MatchesStatusFilter(["Rejected"], ["Accepted", "rejected"]));
        Assert.False(CalendarStateClassifier.MatchesStatusFilter(["Submitted"], ["Accepted", "Rejected"]));
    }

    [Fact]
    public void MatchesStatusFilter_HidesEventsWithoutMatchingProposals()
    {
        Assert.False(CalendarStateClassifier.MatchesStatusFilter([], ["Accepted"]));
        Assert.False(CalendarStateClassifier.MatchesStatusFilter(["Cancelled"], ["Accepted"]));
    }
}