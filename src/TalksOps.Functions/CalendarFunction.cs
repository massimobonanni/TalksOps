using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;

namespace TalksOps.Functions;

/// <summary>Provides annual calendar data for the current user.</summary>
public sealed class CalendarFunction(
    IEventService events,
    ISessionProposalService proposals,
    RequestCurrentUserContext currentUser)
{
    private readonly IEventService events = events ?? throw new ArgumentNullException(nameof(events));
    private readonly ISessionProposalService proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    private readonly RequestCurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <summary>Returns events and their proposal statuses for a calendar year.</summary>
    [Function(nameof(GetCalendar))]
    public async Task<IActionResult> GetCalendar(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "calendar")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.currentUser.TryInitialize(request.Query["userId"].ToString()))
        {
            return new BadRequestObjectResult("A user identifier is required.");
        }

        if (!int.TryParse(request.Query["year"], out var year) || year is < 1 or > 9999)
        {
            return new BadRequestObjectResult("A valid calendar year is required.");
        }

        var allEvents = new List<TalksOps.Core.Domain.Event>();
        var page = 1;
        int totalCount;
        do
        {
            var result = await this.events.SearchAsync(new EventSearchCriteria
            {
                Year = year,
                Page = page++,
                PageSize = 100
            }, cancellationToken);
            allEvents.AddRange(result.Items);
            totalCount = result.TotalCount;
        }
        while (allEvents.Count < totalCount);

        var calendar = new List<CalendarEventDto>(allEvents.Count);
        foreach (var value in allEvents)
        {
            var eventProposals = await this.proposals.ListByEventAsync(value.Id, cancellationToken);
            calendar.Add(new CalendarEventDto(
                value.Id,
                value.Name,
                value.StartDate,
                value.EndDate,
                eventProposals.Select(proposal => proposal.Status.ToString()).ToArray()));
        }

        return new OkObjectResult(calendar);
    }
}