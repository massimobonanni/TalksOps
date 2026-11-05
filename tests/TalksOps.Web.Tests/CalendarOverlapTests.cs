using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class CalendarOverlapTests : BunitContext
{
    [Fact]
    public void OverlappingEvents_ShareOneOutlineAndPreserveStatusesAndActions()
    {
        var year = DateTime.Today.Year;
        var first = CreateEvent("First", new DateOnly(year, 2, 4), new DateOnly(year, 2, 5), "Accepted");
        var second = CreateEvent("Second", first.StartDate, first.EndDate, "Submitted");
        var separate = CreateEvent("Separate", new DateOnly(year, 2, 9), new DateOnly(year, 2, 10), "Rejected");
        var component = this.RenderCalendar([separate, second, first]);

        var group = Assert.Single(component.FindAll(".calendar-overlap-group"));
        Assert.Equal("Events with overlapping dates", group.GetAttribute("aria-label"));
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal(2, group.QuerySelectorAll(".calendar-event").Length);
        Assert.DoesNotContain("Separate", group.TextContent);
        Assert.Single(group.QuerySelectorAll(".state-accepted .calendar-dot.accepted"));
        Assert.Single(group.QuerySelectorAll(".state-submitted .calendar-dot.submitted"));
        Assert.Equal(2, group.QuerySelectorAll(".calendar-details-button").Length);
        Assert.Equal(3, component.FindAll(".calendar-event").Count);
    }

    [Fact]
    public void DisjointEvents_HaveNoOutline()
    {
        var year = DateTime.Today.Year;
        var component = this.RenderCalendar([
            CreateEvent("First", new DateOnly(year, 2, 4), new DateOnly(year, 2, 5)),
            CreateEvent("Second", new DateOnly(year, 2, 6), new DateOnly(year, 2, 7))
        ]);

        Assert.Empty(component.FindAll(".calendar-overlap-group"));
        Assert.Equal(2, component.FindAll(".calendar-event").Count);
    }

    [Fact]
    public void SharedBoundaryDaysAndOverlapChains_ShareOneOutline()
    {
        var year = DateTime.Today.Year;
        var component = this.RenderCalendar([
            CreateEvent("Last", new DateOnly(year, 2, 6), new DateOnly(year, 2, 8)),
            CreateEvent("First", new DateOnly(year, 2, 1), new DateOnly(year, 2, 4)),
            CreateEvent("Middle", new DateOnly(year, 2, 4), new DateOnly(year, 2, 6))
        ]);

        var group = Assert.Single(component.FindAll(".calendar-overlap-group"));
        Assert.Equal(["First", "Middle", "Last"],
            group.QuerySelectorAll("h3").Select(heading => heading.TextContent.Trim()));
    }

    [Fact]
    public void EventsSpanningMonthsAndYears_AppearAndGroupOnlyInApplicableMonths()
    {
        var year = DateTime.Today.Year;
        var component = this.RenderCalendar([
            CreateEvent("Previous year", new DateOnly(year - 1, 12, 30), new DateOnly(year, 1, 2)),
            CreateEvent("January", new DateOnly(year, 1, 2), new DateOnly(year, 1, 2)),
            CreateEvent("Spanning months", new DateOnly(year, 1, 31), new DateOnly(year, 2, 2)),
            CreateEvent("February", new DateOnly(year, 2, 1), new DateOnly(year, 2, 3)),
            CreateEvent("Next year", new DateOnly(year, 12, 31), new DateOnly(year + 1, 1, 2)),
            CreateEvent("December", new DateOnly(year, 12, 30), new DateOnly(year, 12, 31))
        ]);

        var months = component.FindAll(".calendar-month");
        Assert.Equal(12, months.Count);
        Assert.Equal(3, months[0].QuerySelectorAll(".calendar-event").Length);
        Assert.Equal(2, months[0].QuerySelectorAll(".calendar-overlap-group .calendar-event").Length);
        Assert.DoesNotContain("Spanning months", months[0].QuerySelector(".calendar-overlap-group")!.TextContent);
        Assert.Equal(2, months[1].QuerySelectorAll(".calendar-overlap-group .calendar-event").Length);
        Assert.Equal(2, months[11].QuerySelectorAll(".calendar-overlap-group .calendar-event").Length);
        Assert.All(months.Skip(2).Take(9), month => Assert.Empty(month.QuerySelectorAll(".calendar-event")));
    }

    [Fact]
    public void ChangingYear_RecalculatesGroups()
    {
        var year = DateTime.Today.Year;
        var component = this.RenderCalendar([
            CreateEvent("First", new DateOnly(year, 2, 4), new DateOnly(year, 2, 5)),
            CreateEvent("Second", new DateOnly(year, 2, 4), new DateOnly(year, 2, 5))
        ]);
        Assert.Single(component.FindAll(".calendar-overlap-group"));

        component.Find("[aria-label='Next year']").Click();

        component.WaitForAssertion(() => Assert.Contains($"No events in {year + 1}", component.Markup));
        Assert.Empty(component.FindAll(".calendar-overlap-group"));
    }

    [Fact]
    public void StatusFilters_MatchAnySelectedStatusAndCanBeCleared()
    {
        var year = DateTime.Today.Year;
        var component = this.RenderCalendar([
            new(Guid.NewGuid(), "Accepted and submitted", new DateOnly(year, 1, 5), new DateOnly(year, 1, 5), ["Accepted", "Submitted"]),
            CreateEvent("Rejected", new DateOnly(year, 2, 5), new DateOnly(year, 2, 5), "Rejected"),
            new(Guid.NewGuid(), "No proposals", new DateOnly(year, 2, 6), new DateOnly(year, 2, 6), [])
        ]);
        component.FindAll(".calendar-status-filter").Single(button => button.TextContent.Trim() == "Accepted").Click();

        Assert.Equal("true", component.FindAll(".calendar-status-filter").Single(button => button.TextContent.Trim() == "Accepted").GetAttribute("aria-pressed"));
        Assert.Equal(["Accepted and submitted"], component.FindAll(".calendar-event h3").Select(heading => heading.TextContent.Trim()));
        Assert.Contains("No events match selected statuses", component.Find(".calendar-months").TextContent);

        component.FindAll(".calendar-status-filter").Single(button => button.TextContent.Trim() == "Rejected").Click();

        Assert.Equal(["Accepted and submitted", "Rejected"], component.FindAll(".calendar-event h3").Select(heading => heading.TextContent.Trim()));
        component.Find(".calendar-status-filter-clear").Click();

        Assert.Equal(3, component.FindAll(".calendar-event").Count);
        Assert.Empty(component.FindAll(".calendar-status-filter-clear"));
        Assert.Equal("false", component.FindAll(".calendar-status-filter").Single(button => button.TextContent.Trim() == "Accepted").GetAttribute("aria-pressed"));
        Assert.Contains(component.FindAll(".month-empty"), empty => empty.TextContent.Trim() == "No events");
    }

    [Fact]
    public void StatusFilters_WithNoMatchingEvents_ShowFilteredEmptyState()
    {
        var year = DateTime.Today.Year;
        var component = this.RenderCalendar([
            CreateEvent("Cancelled", new DateOnly(year, 2, 5), new DateOnly(year, 2, 5), "Cancelled")
        ]);

        component.FindAll(".calendar-status-filter").Single(button => button.TextContent.Trim() == "Accepted").Click();

        Assert.Contains("No events match the selected statuses", component.Find("[role=status]").TextContent);
        Assert.Empty(component.FindAll(".calendar-month"));
        Assert.DoesNotContain("No events in", component.Markup);
    }

    private IRenderedComponent<Calendar> RenderCalendar(IReadOnlyList<CalendarEventDto> events)
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        var httpClient = new HttpClient(new CalendarHandler(events)) { BaseAddress = new Uri("https://talksops.test/") };
        this.Services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(httpClient, new TestUser()));
        var component = this.Render<Calendar>();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".calendar-event")));
        return component;
    }

    private static CalendarEventDto CreateEvent(string name, DateOnly start, DateOnly end, string status = "Submitted") =>
        new(Guid.NewGuid(), name, start, end, [status]);

    private sealed class TestUser : ICurrentUserContext
    {
        public string UserId => "test-user";
    }

    private sealed class CalendarHandler(IReadOnlyList<CalendarEventDto> events) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var year = int.Parse(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query)["year"].ToString());
            var yearEvents = events.Where(eventItem => eventItem.StartDate.Year <= year && eventItem.EndDate.Year >= year);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(yearEvents.ToArray()) });
        }
    }
}
