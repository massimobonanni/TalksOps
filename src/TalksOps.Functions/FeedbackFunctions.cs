using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Functions;

/// <summary>Speaker-only management and anonymous participant feedback endpoints.</summary>
public sealed class FeedbackFunctions(
    IFeedbackService feedback,
    IEventService events,
    ISessionProposalService proposals,
    RequestCurrentUserContext currentUser,
    TimeProvider clock,
    [FromKeyedServices(FeedbackFunctions.RateLimiterKey)] PartitionedRateLimiter<string> rateLimiter)
{
    /// <summary>The DI key for the anonymous feedback limiter.</summary>
    public const string RateLimiterKey = "AnonymousFeedback";

    /// <summary>The maximum participant comment length.</summary>
    public const int MaxCommentLength = 4000;

    /// <summary>The maximum JSON request size in bytes.</summary>
    public const int MaxRequestBytes = 16384;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Gets settings after verifying the authenticated speaker owns the session.</summary>
    [Function(nameof(GetFeedbackSettings))]
    public async Task<IActionResult> GetFeedbackSettings(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "events/{eventId}/proposals/{proposalId}/feedback/settings")] HttpRequest request,
        string eventId, string proposalId, CancellationToken cancellationToken)
    {
        if (!currentUser.TryInitialize(request.Query["userId"].ToString()))
        {
            return InvalidRequest("A user identifier is required.");
        }

        if (!TryReadIds(eventId, proposalId, out var parentId, out var id))
        {
            return InvalidRequest("The event or proposal identifier is invalid.");
        }

        if (!await this.OwnsSessionAsync(parentId, id, cancellationToken))
        {
            return new NotFoundResult();
        }

        var settings = await feedback.GetSettingsAsync(id, cancellationToken);
        return settings is null ? new NotFoundResult() : new OkObjectResult(ToDto(settings));
    }

    /// <summary>Saves settings using routing data from the verified session, not the payload.</summary>
    [Function(nameof(SaveFeedbackSettings))]
    public async Task<IActionResult> SaveFeedbackSettings(
        [HttpTrigger(AuthorizationLevel.Function, "put",
            Route = "events/{eventId}/proposals/{proposalId}/feedback/settings")] HttpRequest request,
        string eventId, string proposalId, CancellationToken cancellationToken)
    {
        if (!TryReadIds(eventId, proposalId, out var parentId, out var id))
        {
            return InvalidRequest("The event or proposal identifier is invalid.");
        }

        var body = await ReadBodyAsync<ApiRequest<SaveFeedbackSettingsRequest>>(request, cancellationToken);
        if (body.Error is not null)
        {
            return body.Error;
        }

        if (body.Value?.Payload is null || !currentUser.TryInitialize(body.Value.UserId))
        {
            return InvalidRequest("A user identifier and settings payload are required.");
        }

        if (!await this.OwnsSessionAsync(parentId, id, cancellationToken))
        {
            return new NotFoundResult();
        }

        var payload = body.Value.Payload;
        var settings = new FeedbackSettings
        {
            SessionId = id,
            EventId = parentId,
            ValidFrom = payload.ValidFrom,
            ValidTo = payload.ValidTo,
            TimeZoneId = payload.TimeZoneId
        };
        var errors = FeedbackRules.Validate(settings);
        if (errors.Count > 0)
        {
            return InvalidRequest(string.Join(" ", errors));
        }

        try
        {
            return new OkObjectResult(ToDto(await feedback.SaveSettingsAsync(settings, cancellationToken)));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    /// <summary>Lists feedback for the owning speaker even outside the public validity window.</summary>
    [Function(nameof(ListFeedback))]
    public async Task<IActionResult> ListFeedback(
        [HttpTrigger(AuthorizationLevel.Function, "get",
            Route = "events/{eventId}/proposals/{proposalId}/feedback")] HttpRequest request,
        string eventId, string proposalId, CancellationToken cancellationToken)
    {
        if (!currentUser.TryInitialize(request.Query["userId"].ToString()))
        {
            return InvalidRequest("A user identifier is required.");
        }

        if (!TryReadIds(eventId, proposalId, out var parentId, out var id))
        {
            return InvalidRequest("The event or proposal identifier is invalid.");
        }

        if (!await this.OwnsSessionAsync(parentId, id, cancellationToken))
        {
            return new NotFoundResult();
        }

        var values = await feedback.ListBySessionAsync(id, cancellationToken);
        return new OkObjectResult(values.Select(value => new SessionFeedbackDto(
            value.Id, value.SessionId, value.SessionRating, value.SpeakerRating, value.Comment,
            value.Timestamp, new FeedbackMetadataDto(value.Metadata.IpAddress, value.Metadata.UserAgent))).ToArray());
    }

    /// <summary>Exposes only a title and availability during the server-authoritative window.</summary>
    [Function(nameof(GetPublicFeedback))]
    public async Task<IActionResult> GetPublicFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "feedback/{sessionId}")] HttpRequest request,
        string sessionId, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        using var lease = rateLimiter.AttemptAcquire(RateLimitPartitionKey(request));
        if (!lease.IsAcquired)
        {
            return RateLimited(request, lease);
        }

        if (!Guid.TryParse(sessionId, out var id) || id == Guid.Empty)
        {
            return new NotFoundResult();
        }

        var titles = await this.GetAvailableTitlesAsync(id, cancellationToken);
        return titles is null
            ? new NotFoundResult()
            : new OkObjectResult(new PublicFeedbackDto(titles.Value.EventTitle, titles.Value.SessionTitle, true));
    }

    /// <summary>Accepts ratings and comment only; derives metadata from the server request.</summary>
    [Function(nameof(SubmitFeedback))]
    public async Task<IActionResult> SubmitFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "feedback/{sessionId}")] HttpRequest request,
        string sessionId, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        using var lease = rateLimiter.AttemptAcquire(RateLimitPartitionKey(request));
        if (!lease.IsAcquired)
        {
            return RateLimited(request, lease);
        }

        if (!Guid.TryParse(sessionId, out var id) || id == Guid.Empty
            || await this.GetAvailableTitlesAsync(id, cancellationToken) is null)
        {
            return new NotFoundResult();
        }

        var body = await ReadBodyAsync<SubmitFeedbackRequest>(request, cancellationToken);
        if (body.Error is not null)
        {
            return body.Error;
        }

        if (body.Value is null)
        {
            return InvalidRequest("Ratings and an optional comment are required.");
        }

        var submission = new SessionFeedbackSubmission
        {
            SessionRating = body.Value.SessionRating,
            SpeakerRating = body.Value.SpeakerRating,
            Comment = body.Value.Comment
        };
        var errors = FeedbackRules.ValidateSubmission(submission);
        if (errors.Count > 0 || submission.Comment?.Length > MaxCommentLength)
        {
            return InvalidRequest(errors.Count > 0
                ? string.Join(" ", errors)
                : $"The comment cannot exceed {MaxCommentLength} characters.");
        }

        // Recheck after reading the body: an already-open form cannot submit after closing.
        if (await this.GetAvailableTitlesAsync(id, cancellationToken) is null)
        {
            return new NotFoundResult();
        }

        try
        {
            await feedback.CreateAsync(id, submission, new SessionFeedbackMetadata
            {
                IpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = request.Headers.UserAgent.ToString()
            }, cancellationToken);
            return new StatusCodeResult(StatusCodes.Status201Created);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    private async Task<bool> OwnsSessionAsync(Guid eventId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await events.GetAsync(eventId, cancellationToken) is null)
        {
            return false;
        }

        var session = await proposals.GetAsync(eventId, sessionId, cancellationToken);
        return session is not null && session.Id == sessionId && session.EventId == eventId
            && string.Equals(session.OwnerId, currentUser.UserId, StringComparison.Ordinal);
    }

    private async Task<(string EventTitle, string SessionTitle)?> GetAvailableTitlesAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        var settings = await feedback.GetSettingsAsync(sessionId, cancellationToken);
        if (!this.IsAvailable(settings))
        {
            return null;
        }

        var titles = await feedback.GetSessionTitlesAsync(sessionId, cancellationToken);
        settings = await feedback.GetSettingsAsync(sessionId, cancellationToken);
        return this.IsAvailable(settings) && titles.EventTitle is not null && titles.SessionTitle is not null
            ? (titles.EventTitle, titles.SessionTitle)
            : null;
    }

    private bool IsAvailable(FeedbackSettings? settings)
    {
        var now = clock.GetUtcNow();
        return settings is not null && settings.ValidFrom <= now && now < settings.ValidTo;
    }

    private static bool TryReadIds(string eventId, string proposalId, out Guid parentId, out Guid id)
    {
        var validParent = Guid.TryParse(eventId, out parentId) && parentId != Guid.Empty;
        var validId = Guid.TryParse(proposalId, out id) && id != Guid.Empty;
        return validParent && validId;
    }

    private static async Task<(T? Value, IActionResult? Error)> ReadBodyAsync<T>(
        HttpRequest request, CancellationToken cancellationToken) where T : class
    {
        if (request.ContentLength > MaxRequestBytes)
        {
            return (null, TooLarge());
        }

        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int count;
        while ((count = await request.Body.ReadAsync(bytes, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxRequestBytes)
            {
                return (null, TooLarge());
            }

            buffer.Write(bytes, 0, count);
        }

        buffer.Position = 0;
        try
        {
            return (await JsonSerializer.DeserializeAsync<T>(buffer, JsonOptions, cancellationToken), null);
        }
        catch (JsonException)
        {
            return (null, InvalidRequest("The request body is not valid JSON."));
        }
    }

    private static string RateLimitPartitionKey(HttpRequest request) =>
        request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ObjectResult RateLimited(HttpRequest request, RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            request.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ObjectResult(new ApiErrorDto("rate_limited", "Please wait before retrying."))
        {
            StatusCode = StatusCodes.Status429TooManyRequests
        };
    }

    private static ObjectResult TooLarge() =>
        new(new ApiErrorDto("request_too_large", $"The request cannot exceed {MaxRequestBytes} bytes."))
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge
        };

    private static BadRequestObjectResult InvalidRequest(string message) =>
        new(new ApiErrorDto("invalid_request", message));

    private static FeedbackSettingsDto ToDto(FeedbackSettings value) =>
        new(value.SessionId, value.ValidFrom, value.ValidTo, value.TimeZoneId);
}
