using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class EventsProposalSummaryTests : BunitContext
{
    [Fact]
    public void InitialLoad_ShowsEveryStatusBeforeExpandingIncludingDuplicates()
    {
        using var handler = new ProposalHandler(ProposalStatus.Submitted, ProposalStatus.Accepted,
            ProposalStatus.Rejected, ProposalStatus.Cancelled, ProposalStatus.Accepted);
        var component = this.RenderEvents(handler);

        component.WaitForAssertion(() => Assert.Contains("Manage proposals (5)", SummaryText(component)));
        Assert.Equal(5, component.FindAll(".proposal-indicators .calendar-dot").Count);
        Assert.Equal(2, component.FindAll(".proposal-indicators .accepted").Count);
        Assert.Single(component.FindAll(".proposal-indicators .submitted"));
        Assert.Single(component.FindAll(".proposal-indicators .rejected"));
        Assert.Single(component.FindAll(".proposal-indicators .cancelled"));
        Assert.Equal("Proposal status: Submitted", component.Find(".proposal-indicators .submitted").GetAttribute("aria-label"));
        Assert.Equal("Submitted", component.Find(".proposal-indicators .submitted").GetAttribute("title"));
        Assert.Empty(component.FindAll(".detail-row"));
        Assert.Equal(1, handler.CalendarRequests);
        Assert.Equal(0, handler.ProposalRequests);
    }

    [Fact]
    public void NoProposals_ShowsZeroWithoutCircles()
    {
        using var handler = new ProposalHandler();
        var component = this.RenderEvents(handler);

        component.WaitForAssertion(() => Assert.Contains("Manage proposals (0)", SummaryText(component)));
        Assert.Empty(component.FindAll(".proposal-indicators .calendar-dot"));
    }

    [Fact]
    public void EmptySearch_DoesNotRequestCalendar()
    {
        using var handler = new ProposalHandler { HasEvents = false };
        var component = this.RenderEvents(handler);

        component.WaitForAssertion(() => Assert.Contains("No events found", component.Markup));
        Assert.Equal(0, handler.CalendarRequests);
    }

    [Fact]
    public void InvalidEventDates_ShowsValidationErrorInsideEventDialog()
    {
        using var handler = new ProposalHandler();
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("Manage proposals", component.Markup));
        component.Find(".page-heading button").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".event-dialog")));
        var dateInputs = component.FindAll(".event-dialog input[type=date]");
        dateInputs[0].Change("2026-10-15");
        component.FindAll(".event-dialog input[type=date]")[1].Change("2026-10-14");

        component.Find(".event-dialog form").Submit();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("The end date cannot be before the start date.", component.Find(".event-dialog [role=alert]").TextContent);
            Assert.Single(component.FindAll(".notice-error"));
            Assert.Single(component.FindAll(".event-dialog .notice-error"));
        });
    }

    [Fact]
    public void AddEditAndDelete_RefreshCountsAndCirclesWithoutAnotherCalendarRequest()
    {
        using var handler = new ProposalHandler();
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("(0)", SummaryText(component)));
        component.Find(".proposal-summary button").Click();
        component.WaitForAssertion(() => Assert.Contains("Hide proposals (0)", SummaryText(component)));
        component.Find(".detail-heading button").Click();
        component.Find(".editor-form input[maxlength='200']").Change("New session");
        component.Find(".editor-form").Submit();

        component.WaitForAssertion(() => Assert.Contains("Hide proposals (1)", SummaryText(component)));
        Assert.Single(component.FindAll(".proposal-indicators .submitted"));
        component.Find(".proposal-row .row-actions button").Click();
        component.Find(".editor-form input[maxlength='200']").Change("Edited session");
        component.Find(".editor-form").Submit();
        component.WaitForAssertion(() => Assert.Equal("Edited session", component.Find(".proposal-row h3").TextContent));
        Assert.Single(component.FindAll(".proposal-indicators .calendar-dot"));

        this.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        component.Find(".proposal-row .danger").Click();
        component.WaitForAssertion(() => Assert.Contains("Hide proposals (0)", SummaryText(component)));
        Assert.Empty(component.FindAll(".proposal-indicators .calendar-dot"));
        Assert.Empty(component.FindAll(".proposal-row"));
        component.Find(".proposal-summary button").Click();
        Assert.Contains("Manage proposals (0)", SummaryText(component));
        Assert.Equal(1, handler.CalendarRequests);
    }

    [Theory]
    [InlineData(ProposalStatus.Accepted, "accepted")]
    [InlineData(ProposalStatus.Rejected, "rejected")]
    [InlineData(ProposalStatus.Cancelled, "cancelled")]
    [InlineData(ProposalStatus.Submitted, "submitted")]
    public void ChangeStatus_UpdatesCircleAndPreservesCount(ProposalStatus status, string cssClass)
    {
        using var handler = new ProposalHandler(ProposalStatus.Submitted);
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("(1)", SummaryText(component)));
        component.Find(".proposal-summary button").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".proposal-row")));

        component.Find(".status-control select").Change(((int)status).ToString());

        component.WaitForAssertion(() => Assert.Single(component.FindAll($".proposal-indicators .{cssClass}")));
        Assert.Contains("Hide proposals (1)", SummaryText(component));
        Assert.Equal(status.ToString(), component.Find(".status-pill").TextContent);
        Assert.Equal(1, handler.CalendarRequests);
    }

    [Fact]
    public void Search_RefreshesSummaryAndExpandedDetailsFromExternalChanges()
    {
        using var handler = new ProposalHandler(ProposalStatus.Submitted);
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("(1)", SummaryText(component)));
        component.Find(".proposal-summary button").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".proposal-row")));
        handler.Proposals.Clear();

        component.Find(".filter-bar").Submit();

        component.WaitForAssertion(() => Assert.Contains("Hide proposals (0)", SummaryText(component)));
        Assert.Empty(component.FindAll(".proposal-row"));
        Assert.Empty(component.FindAll(".proposal-indicators .calendar-dot"));
        Assert.Equal(2, handler.CalendarRequests);
    }

    [Fact]
    public void UnknownStatus_RemainsCountedWithNeutralIndicator()
    {
        using var handler = new ProposalHandler { CalendarStatuses = ["FutureStatus"] };
        var component = this.RenderEvents(handler);

        component.WaitForAssertion(() => Assert.Contains("Manage proposals (1)", SummaryText(component)));
        Assert.Single(component.FindAll(".proposal-indicators .unknown"));
        Assert.Equal("Proposal status: FutureStatus", component.Find(".unknown").GetAttribute("aria-label"));
    }

    [Fact]
    public void SearchWithoutMatchingEvent_RemovesExpandedDetailsAndEditor()
    {
        using var handler = new ProposalHandler(ProposalStatus.Submitted);
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("(1)", SummaryText(component)));
        component.Find(".proposal-summary button").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".proposal-row")));
        component.Find(".detail-heading button").Click();
        handler.HasEvents = false;

        component.Find(".filter-bar").Submit();

        component.WaitForAssertion(() => Assert.Contains("No events found", component.Markup));
        Assert.Empty(component.FindAll(".detail-row"));
        Assert.Empty(component.FindAll(".editor-form"));
        Assert.Equal(1, handler.CalendarRequests);
    }

    [Fact]
    public void CalendarFailure_DoesNotInventZeroAndCanRecoverByOpeningProposals()
    {
        using var handler = new ProposalHandler(ProposalStatus.Accepted) { FailCalendar = true };
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("Unable to load proposal summaries", component.Markup));
        Assert.Equal("Manage proposals", SummaryText(component));
        Assert.Empty(component.FindAll(".proposal-indicators .calendar-dot"));

        component.Find(".proposal-summary button").Click();

        component.WaitForAssertion(() => Assert.Contains("Hide proposals (1)", SummaryText(component)));
        Assert.Single(component.FindAll(".proposal-indicators .accepted"));
        handler.FailCalendar = false;
        component.Find(".filter-bar").Submit();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".notice-error")));
    }

    [Fact]
    public void RefreshFailure_PreservesLastKnownSummaryAndReportsError()
    {
        using var handler = new ProposalHandler(ProposalStatus.Submitted);
        var component = this.RenderEvents(handler);
        component.WaitForAssertion(() => Assert.Contains("(1)", SummaryText(component)));
        component.Find(".proposal-summary button").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".proposal-row")));
        handler.FailProposals = true;

        component.Find(".status-control select").Change(((int)ProposalStatus.Accepted).ToString());

        component.WaitForAssertion(() => Assert.Contains("Unable to change proposal status", component.Markup));
        Assert.Contains("Hide proposals (1)", SummaryText(component));
        Assert.Single(component.FindAll(".proposal-indicators .submitted"));
    }

    private IRenderedComponent<Events> RenderEvents(ProposalHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://talksops.test/") };
        this.Services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(httpClient, new TestUser()));
        return this.Render<Events>();
    }

    private static string SummaryText(IRenderedComponent<Events> component) =>
        string.Join(' ', component.Find(".proposal-summary button").TextContent.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class TestUser : ICurrentUserContext
    {
        public string UserId => "test-user";
    }

    private sealed class ProposalHandler : HttpMessageHandler
    {
        private readonly EventDto eventItem = new(Guid.NewGuid(), "test-user", "Summit", "Rome",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2), new Dictionary<string, decimal>());

        public ProposalHandler(params ProposalStatus[] statuses)
        {
            this.Proposals = statuses.Select(status => this.NewProposal(status)).ToList();
        }

        public List<SessionProposalDto> Proposals { get; }
        public int CalendarRequests { get; private set; }
        public int ProposalRequests { get; private set; }
        public bool HasEvents { get; set; } = true;
        public bool FailCalendar { get; set; }
        public bool FailProposals { get; set; }
        public IReadOnlyList<string>? CalendarStatuses { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/events")
            {
                return JsonResponse(new PagedResponse<EventDto>(this.HasEvents ? [this.eventItem] : [], 1, 20, this.HasEvents ? 1 : 0));
            }

            if (path == "/api/calendar")
            {
                this.CalendarRequests++;
                return this.FailCalendar ? new(HttpStatusCode.ServiceUnavailable) : JsonResponse(new[]
                {
                    new CalendarEventDto(this.eventItem.Id, this.eventItem.Name, this.eventItem.StartDate,
                        this.eventItem.EndDate, this.CalendarStatuses ?? this.Proposals.Select(proposal => proposal.Status.ToString()).ToArray())
                });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/proposals", StringComparison.Ordinal))
            {
                this.ProposalRequests++;
                return this.FailProposals ? new(HttpStatusCode.ServiceUnavailable) : JsonResponse(this.Proposals);
            }

            if (request.Method == HttpMethod.Delete)
            {
                var proposalId = Guid.Parse(path.Split('/')[^1]);
                this.Proposals.RemoveAll(proposal => proposal.Id == proposalId);
                return new(HttpStatusCode.NoContent);
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var payload = body.RootElement.GetProperty("payload");
            if (request.Method == HttpMethod.Post)
            {
                var proposal = this.NewProposal(ProposalStatus.Submitted) with { Title = payload.GetProperty("title").GetString()! };
                this.Proposals.Add(proposal);
                return JsonResponse(proposal);
            }

            var segments = path.Split('/');
            var id = Guid.Parse(segments[request.Method == HttpMethod.Patch ? ^2 : ^1]);
            var index = this.Proposals.FindIndex(proposal => proposal.Id == id);
            this.Proposals[index] = request.Method == HttpMethod.Patch
                ? this.Proposals[index] with { Status = (ProposalStatus)payload.GetProperty("status").GetInt32() }
                : this.Proposals[index] with { Title = payload.GetProperty("title").GetString()! };
            return JsonResponse(this.Proposals[index]);
        }

        private SessionProposalDto NewProposal(ProposalStatus status) => new(Guid.NewGuid(), this.eventItem.Id,
            "test-user", "Session", "Abstract", "", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

        private static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}