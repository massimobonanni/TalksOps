using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Functions;

/// <summary>HTTP endpoints for events and their session proposals.</summary>
public sealed class EventsFunctions(
    IEventService events,
    ISessionProposalService proposals,
    RequestCurrentUserContext currentUser)
{
    private readonly IEventService events = events ?? throw new ArgumentNullException(nameof(events));
    private readonly ISessionProposalService proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    private readonly RequestCurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <summary>Searches events for the specified user.</summary>
    [Function(nameof(SearchEvents))]
    public async Task<IActionResult> SearchEvents(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "events")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.TrySetQueryUser(request, out var error))
        {
            return error!;
        }

        if (!TryReadOptionalInt(request, "year", out var year)
            || !TryReadInt(request, "page", 1, out var page)
            || !TryReadInt(request, "pageSize", 20, out var pageSize)
            || year is < 1 or > 9999
            || page < 1
            || pageSize < 1)
        {
            return BadRequest("Search filters must contain valid year, page, and pageSize values.");
        }

        var result = await this.events.SearchAsync(new EventSearchCriteria
        {
            Year = year,
            Name = request.Query["name"].ToString(),
            Location = request.Query["location"].ToString(),
            Page = page,
            PageSize = pageSize
        }, cancellationToken);
        return new OkObjectResult(new PagedResponse<EventDto>(
            result.Items.Select(ToDto).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }

    /// <summary>Gets an event owned by the specified user.</summary>
    [Function(nameof(GetEvent))]
    public async Task<IActionResult> GetEvent(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "events/{eventId}")] HttpRequest request,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!this.TrySetQueryUser(request, out var error))
        {
            return error!;
        }

        if (!Guid.TryParse(eventId, out var id))
        {
            return BadRequest("The event identifier is invalid.");
        }

        var value = await this.events.GetAsync(id, cancellationToken);
        return value is null ? new NotFoundResult() : new OkObjectResult(ToDto(value));
    }

    /// <summary>Creates an event for the specified user.</summary>
    [Function(nameof(CreateEvent))]
    public async Task<IActionResult> CreateEvent(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "events")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var envelope = await ReadEnvelopeAsync<SaveEventRequest>(request, cancellationToken);
        if (envelope.Error is not null)
        {
            return envelope.Error;
        }

        if (!this.TrySetBodyUser(envelope.Value!, out var error))
        {
            return error!;
        }

        try
        {
            var created = await this.events.CreateAsync(ToDomain(envelope.Value!.Payload), cancellationToken);
            return new CreatedResult($"events/{created.Id}", ToDto(created));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>Updates an event owned by the specified user.</summary>
    [Function(nameof(UpdateEvent))]
    public async Task<IActionResult> UpdateEvent(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "events/{eventId}")] HttpRequest request,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(eventId, out var id))
        {
            return BadRequest("The event identifier is invalid.");
        }

        var envelope = await ReadEnvelopeAsync<SaveEventRequest>(request, cancellationToken);
        if (envelope.Error is not null)
        {
            return envelope.Error;
        }

        if (!this.TrySetBodyUser(envelope.Value!, out var error))
        {
            return error!;
        }

        try
        {
            var updated = await this.events.UpdateAsync(ToDomain(envelope.Value!.Payload) with { Id = id }, cancellationToken);
            return updated is null ? new NotFoundResult() : new OkObjectResult(ToDto(updated));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>Deletes an event and its proposals for the specified user.</summary>
    [Function(nameof(DeleteEvent))]
    public async Task<IActionResult> DeleteEvent(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "events/{eventId}")] HttpRequest request,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!this.TrySetQueryUser(request, out var error))
        {
            return error!;
        }

        if (!Guid.TryParse(eventId, out var id))
        {
            return BadRequest("The event identifier is invalid.");
        }

        return await this.events.DeleteAsync(id, cancellationToken) ? new NoContentResult() : new NotFoundResult();
    }

    /// <summary>Lists proposals belonging to an event owned by the specified user.</summary>
    [Function(nameof(ListProposals))]
    public async Task<IActionResult> ListProposals(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "events/{eventId}/proposals")] HttpRequest request,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!this.TrySetQueryUser(request, out var error))
        {
            return error!;
        }

        if (!Guid.TryParse(eventId, out var id))
        {
            return BadRequest("The event identifier is invalid.");
        }

        if (await this.events.GetAsync(id, cancellationToken) is null)
        {
            return new NotFoundResult();
        }

        var values = await this.proposals.ListByEventAsync(id, cancellationToken);
        return new OkObjectResult(values.Select(ToDto).ToArray());
    }

    /// <summary>Gets a proposal from an event owned by the specified user.</summary>
    [Function(nameof(GetProposal))]
    public async Task<IActionResult> GetProposal(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "events/{eventId}/proposals/{proposalId}")] HttpRequest request,
        string eventId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        if (!this.TrySetQueryUser(request, out var error))
        {
            return error!;
        }

        if (!Guid.TryParse(eventId, out var parentId) || !Guid.TryParse(proposalId, out var id))
        {
            return BadRequest("The event or proposal identifier is invalid.");
        }

        var value = await this.proposals.GetAsync(parentId, id, cancellationToken);
        return value is null ? new NotFoundResult() : new OkObjectResult(ToDto(value));
    }

    /// <summary>Creates a proposal under an event owned by the specified user.</summary>
    [Function(nameof(CreateProposal))]
    public async Task<IActionResult> CreateProposal(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "events/{eventId}/proposals")] HttpRequest request,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(eventId, out var parentId))
        {
            return BadRequest("The event identifier is invalid.");
        }

        var envelope = await ReadEnvelopeAsync<SaveSessionProposalRequest>(request, cancellationToken);
        if (envelope.Error is not null)
        {
            return envelope.Error;
        }

        if (!this.TrySetBodyUser(envelope.Value!, out var error))
        {
            return error!;
        }

        if (await this.events.GetAsync(parentId, cancellationToken) is null)
        {
            return new NotFoundResult();
        }

        try
        {
            var created = await this.proposals.CreateAsync(ToDomain(envelope.Value!.Payload, parentId), cancellationToken);
            return new CreatedResult($"events/{parentId}/proposals/{created.Id}", ToDto(created));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>Updates a proposal under an event owned by the specified user.</summary>
    [Function(nameof(UpdateProposal))]
    public async Task<IActionResult> UpdateProposal(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "events/{eventId}/proposals/{proposalId}")] HttpRequest request,
        string eventId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(eventId, out var parentId) || !Guid.TryParse(proposalId, out var id))
        {
            return BadRequest("The event or proposal identifier is invalid.");
        }

        var envelope = await ReadEnvelopeAsync<SaveSessionProposalRequest>(request, cancellationToken);
        if (envelope.Error is not null)
        {
            return envelope.Error;
        }

        if (!this.TrySetBodyUser(envelope.Value!, out var error))
        {
            return error!;
        }

        try
        {
            var updated = await this.proposals.UpdateAsync(
                ToDomain(envelope.Value!.Payload, parentId) with { Id = id }, cancellationToken);
            return updated is null ? new NotFoundResult() : new OkObjectResult(ToDto(updated));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>Changes a proposal's lifecycle status.</summary>
    [Function(nameof(ChangeProposalStatus))]
    public async Task<IActionResult> ChangeProposalStatus(
        [HttpTrigger(AuthorizationLevel.Function, "patch", Route = "events/{eventId}/proposals/{proposalId}/status")] HttpRequest request,
        string eventId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(eventId, out var parentId) || !Guid.TryParse(proposalId, out var id))
        {
            return BadRequest("The event or proposal identifier is invalid.");
        }

        var envelope = await ReadEnvelopeAsync<ChangeProposalStatusRequest>(request, cancellationToken);
        if (envelope.Error is not null)
        {
            return envelope.Error;
        }

        if (!this.TrySetBodyUser(envelope.Value!, out var error))
        {
            return error!;
        }

        if (!Enum.IsDefined(envelope.Value!.Payload.Status))
        {
            return BadRequest("The proposal status is invalid.");
        }

        try
        {
            var updated = await this.proposals.ChangeStatusAsync(parentId, id, envelope.Value.Payload.Status, cancellationToken);
            return updated is null ? new NotFoundResult() : new OkObjectResult(ToDto(updated));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }

    /// <summary>Deletes a proposal owned by the specified user.</summary>
    [Function(nameof(DeleteProposal))]
    public async Task<IActionResult> DeleteProposal(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "events/{eventId}/proposals/{proposalId}")] HttpRequest request,
        string eventId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        if (!this.TrySetQueryUser(request, out var error))
        {
            return error!;
        }

        if (!Guid.TryParse(eventId, out var parentId) || !Guid.TryParse(proposalId, out var id))
        {
            return BadRequest("The event or proposal identifier is invalid.");
        }

        try
        {
            return await this.proposals.DeleteAsync(parentId, id, cancellationToken)
                ? new NoContentResult()
                : new NotFoundResult();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }

    private bool TrySetQueryUser(HttpRequest request, out IActionResult? error)
    {
        if (this.currentUser.TryInitialize(request.Query["userId"].ToString()))
        {
            error = null;
            return true;
        }

        error = BadRequest("A user identifier is required.");
        return false;
    }

    private bool TrySetBodyUser<T>(ApiRequest<T> envelope, out IActionResult? error)
    {
        if (this.currentUser.TryInitialize(envelope.UserId))
        {
            error = null;
            return true;
        }

        error = BadRequest("A user identifier is required.");
        return false;
    }

    private static async Task<(ApiRequest<T>? Value, IActionResult? Error)> ReadEnvelopeAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var envelope = await request.ReadFromJsonAsync<ApiRequest<T>>(cancellationToken);
            return envelope?.Payload is null
                ? (null, BadRequest("A valid request envelope and payload are required."))
                : (envelope, null);
        }
        catch (JsonException)
        {
            return (null, BadRequest("The request body is not valid JSON."));
        }
    }

    private static bool TryReadOptionalInt(HttpRequest request, string name, out int? value)
    {
        var rawValue = request.Query[name].ToString();
        if (string.IsNullOrEmpty(rawValue))
        {
            value = null;
            return true;
        }

        var valid = int.TryParse(rawValue, out var parsed);
        value = valid ? parsed : null;
        return valid;
    }

    private static bool TryReadInt(HttpRequest request, string name, int defaultValue, out int value)
    {
        var rawValue = request.Query[name].ToString();
        if (string.IsNullOrEmpty(rawValue))
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(rawValue, out value);
    }

    private static Event ToDomain(SaveEventRequest value) => new()
    {
        OwnerId = string.Empty,
        Name = value.Name,
        Location = value.Location,
        StartDate = value.StartDate,
        EndDate = value.EndDate,
        Costs = value.Costs ?? new Dictionary<string, decimal>()
    };

    private static SessionProposal ToDomain(SaveSessionProposalRequest value, Guid eventId) => new()
    {
        EventId = eventId,
        OwnerId = string.Empty,
        Title = value.Title,
        Abstract = value.Abstract,
        Notes = value.Notes
    };

    private static EventDto ToDto(Event value) => new(
        value.Id, value.OwnerId, value.Name, value.Location, value.StartDate, value.EndDate, value.Costs);

    private static SessionProposalDto ToDto(SessionProposal value) => new(
        value.Id, value.EventId, value.OwnerId, value.Title, value.Abstract, value.Notes,
        value.Status, value.CreatedAt, value.UpdatedAt);

    private static BadRequestObjectResult BadRequest(string message) => new(message);

    private static ConflictObjectResult Conflict(string message) => new(message);
}