using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class CalendarEventDetailsTests : BunitContext
{
    [Fact]
    public void Open_LoadsDetailsOnDemandAndShowsEveryProposalStatus()
    {
        using var handler = new DetailsHandler();
        var component = this.RenderCalendar(handler);
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".calendar-details-button")));
        Assert.Equal(0, handler.EventRequests);
        Assert.Equal(0, handler.ProposalRequests);

        component.Find(".calendar-details-button").Click();

        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll(".calendar-session").Count));
        Assert.Contains("Rome", component.Find("dialog").TextContent);
        Assert.Contains("Session abstract", component.Find("dialog").TextContent);
        Assert.Equal(1, handler.EventRequests);
        Assert.Equal(1, handler.ProposalRequests);
        foreach (var status in Enum.GetValues<ProposalStatus>())
        {
            Assert.Contains(status.ToString(), component.Find("dialog").TextContent);
        }
    }

    [Fact]
    public void EmptyProposalsAndOptionalFields_ShowExplicitEmptyStates()
    {
        using var handler = new DetailsHandler { HasProposals = false };
        var component = this.RenderCalendar(handler);
        component.Find(".calendar-details-button").Click();

        component.WaitForAssertion(() => Assert.Contains("No session proposals", component.Find("dialog").TextContent));
        Assert.Contains("Not specified", component.Find("dialog").TextContent);
        Assert.Empty(component.FindAll(".calendar-session"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApiFailure_ShowsLocalErrorAndRetryRecovers(bool failProposals)
    {
        using var handler = new DetailsHandler { FailEvent = !failProposals, FailProposals = failProposals };
        var component = this.RenderCalendar(handler);
        component.Find(".calendar-details-button").Click();

        component.WaitForAssertion(() => Assert.Contains("Unable to load event details", component.Find("dialog [role=alert]").TextContent));
        Assert.DoesNotContain("No session proposals", component.Find("dialog").TextContent);
        Assert.Single(component.FindAll(".calendar-event"));
        handler.FailEvent = false;
        handler.FailProposals = false;
        component.Find("dialog .button").Click();

        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll(".calendar-session").Count));
        Assert.Empty(component.FindAll("dialog [role=alert]"));
        Assert.Equal(2, handler.EventRequests);
    }

    [Fact]
    public void MissingEvent_ShowsUnavailableMessage()
    {
        using var handler = new DetailsHandler { MissingEvent = true };
        var component = this.RenderCalendar(handler);
        component.Find(".calendar-details-button").Click();

        component.WaitForAssertion(() => Assert.Contains("no longer available", component.Find("dialog [role=alert]").TextContent));
        Assert.Empty(component.FindAll(".calendar-session"));
    }

    [Fact]
    public void CloseAndReopen_RefreshesDetailsAndPreservesCalendarIndicators()
    {
        using var handler = new DetailsHandler();
        var component = this.RenderCalendar(handler);
        component.Find(".calendar-details-button").Click();
        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll(".calendar-session").Count));

        component.Find(".calendar-details-close").Click();

        Assert.Empty(component.FindAll(".calendar-session"));
        Assert.Single(component.FindAll(".calendar-event .submitted"));
        component.Find(".calendar-details-button").Click();
        component.WaitForAssertion(() => Assert.Equal(2, handler.EventRequests));
    }

    [Fact]
    public async Task CloseDuringLoad_LateResponseDoesNotRestoreDetails()
    {
        using var handler = new DetailsHandler { DelayedEvent = new() };
        var component = this.RenderCalendar(handler);
        var clickTask = component.Find(".calendar-details-button").ClickAsync(new MouseEventArgs());
        component.WaitForAssertion(() => Assert.Contains("Loading event details", component.Find("dialog [role=status]").TextContent));

        component.Find(".calendar-details-close").Click();
        handler.DelayedEvent.SetResult(DetailsHandler.JsonResponse(handler.Event));
        await clickTask;

        Assert.Empty(component.FindAll("#calendar-details-title"));
        Assert.Empty(component.FindAll(".calendar-session"));
    }

    [Fact]
    public async Task SelectAnotherEvent_LateResponseCannotReplaceCurrentDetails()
    {
        using var handler = new DetailsHandler { DelayedEvent = new(), IncludeSecondEvent = true };
        var component = this.RenderCalendar(handler);
        var clickTask = component.FindAll(".calendar-details-button")[0].ClickAsync(new MouseEventArgs());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("dialog [role=status]")));

        await component.FindAll(".calendar-details-button")[1].ClickAsync(new MouseEventArgs());
        component.WaitForAssertion(() => Assert.Equal("Second summit", component.Find("#calendar-details-title").TextContent));
        handler.DelayedEvent.SetResult(DetailsHandler.JsonResponse(handler.Event));
        await clickTask;

        Assert.Equal("Second summit", component.Find("#calendar-details-title").TextContent);
        Assert.Contains("Milan", component.Find("dialog").TextContent);
    }

    [Fact]
    public async Task ChangeYearDuringLoad_InvalidatesPendingDetails()
    {
        using var handler = new DetailsHandler { DelayedEvent = new() };
        var component = this.RenderCalendar(handler);
        var clickTask = component.Find(".calendar-details-button").ClickAsync(new MouseEventArgs());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("dialog [role=status]")));

        component.Find("button[aria-label='Next year']").Click();
        handler.DelayedEvent.SetResult(DetailsHandler.JsonResponse(handler.Event));
        await clickTask;

        Assert.Empty(component.FindAll("#calendar-details-title"));
    }

    [Fact]
    public async Task BrowserDismissal_ClearsDialogState()
    {
        using var handler = new DetailsHandler();
        var component = this.RenderCalendar(handler);
        component.Find(".calendar-details-button").Click();
        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll(".calendar-session").Count));

        await component.Instance.DismissDetailsAsync();

        Assert.Empty(component.FindAll("#calendar-details-title"));
        Assert.Empty(component.FindAll(".calendar-session"));
    }

    private IRenderedComponent<Calendar> RenderCalendar(DetailsHandler handler)
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://talksops.test/") };
        this.Services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(httpClient, new TestUser()));
        return this.Render<Calendar>();
    }

    private sealed class TestUser : ICurrentUserContext
    {
        public string UserId => "test-user";
    }

    private sealed class DetailsHandler : HttpMessageHandler
    {
        public EventDto Event { get; } = new(Guid.NewGuid(), "test-user", "Summit", "Rome",
            new DateOnly(DateTime.Today.Year, 5, 1), new DateOnly(DateTime.Today.Year, 5, 2),
            new Dictionary<string, decimal>() { ["Travel"] = 25m });
        public int EventRequests { get; private set; }
        public int ProposalRequests { get; private set; }
        public bool HasProposals { get; set; } = true;
        public bool FailEvent { get; set; }
        public bool FailProposals { get; set; }
        public bool MissingEvent { get; set; }
        public bool IncludeSecondEvent { get; set; }
        public Guid SecondEventId { get; } = Guid.NewGuid();
        public TaskCompletionSource<HttpResponseMessage>? DelayedEvent { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/calendar")
            {
                var events = new List<CalendarEventDto>
                {
                    new(this.Event.Id, this.Event.Name, this.Event.StartDate, this.Event.EndDate, ["Submitted"])
                };
                if (this.IncludeSecondEvent)
                {
                    events.Add(new(this.SecondEventId, "Second summit", this.Event.StartDate, this.Event.EndDate, []));
                }

                return Task.FromResult(JsonResponse(events));
            }

            if (path.EndsWith("/proposals", StringComparison.Ordinal))
            {
                this.ProposalRequests++;
                if (this.FailProposals)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                return Task.FromResult(JsonResponse((this.HasProposals ? Enum.GetValues<ProposalStatus>() : []).Select(status =>
                    new SessionProposalDto(Guid.NewGuid(), this.Event.Id, "test-user", $"Session {status}",
                        "Session abstract", "Session notes", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null)).ToArray()));
            }

            this.EventRequests++;
            if (path.Contains(this.SecondEventId.ToString(), StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(this.Event with { Id = this.SecondEventId, Name = "Second summit", Location = "Milan" }));
            }

            if (this.DelayedEvent is not null)
            {
                return this.DelayedEvent.Task;
            }

            if (this.MissingEvent || this.FailEvent)
            {
                return Task.FromResult(new HttpResponseMessage(this.MissingEvent ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(JsonResponse(this.Event));
        }

        public static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}