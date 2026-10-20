using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TalksOps.AI;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Extraction;

namespace TalksOps.Functions;

/// <summary>HTTP endpoint for extracting event details from a public CFP page.</summary>
public sealed class EventDataExtractionFunction(
    RequestCurrentUserContext currentUser,
    PartitionedRateLimiter<string> rateLimiter,
    ILogger<EventDataExtractionFunction> logger,
    IEventDataExtractionService? extractionService = null)
{
    private readonly IEventDataExtractionService? extractionService = extractionService;
    private readonly RequestCurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly PartitionedRateLimiter<string> rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    private readonly ILogger<EventDataExtractionFunction> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Extracts event details and returns safe, structured errors for failed requests.</summary>
    [Function(nameof(ExtractEventData))]
    public async Task<IActionResult> ExtractEventData(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "events/extract")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var envelope = await ReadEnvelopeAsync(request, cancellationToken);
        if (envelope is null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "A valid request envelope and source page URL are required.");
        }

        if (!this.currentUser.TryInitialize(envelope.UserId))
        {
            return Error(StatusCodes.Status400BadRequest, "user_required", "A user identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Payload.SourcePageUrl))
        {
            return Error(StatusCodes.Status400BadRequest, "source_url_required", "A source page URL is required.");
        }

        if (this.extractionService is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "extraction_unavailable", "Event extraction is not configured.");
        }

        using var lease = await this.rateLimiter.AcquireAsync(this.currentUser.UserId, 1, cancellationToken);
        if (!lease.IsAcquired)
        {
            request.HttpContext.Response.Headers.RetryAfter = "60";
            return Error(StatusCodes.Status429TooManyRequests, "rate_limited", "Please wait before requesting another event extraction.");
        }

        try
        {
            var result = await this.extractionService.ExtractAsync(
                envelope.Payload.SourcePageUrl,
                this.currentUser.UserId,
                cancellationToken);
            return new OkObjectResult(new ExtractEventDataResultDto(
                result.Name,
                result.Location,
                result.OfficialWebsiteUrl,
                result.CallForPapersUrl,
                result.CallForPapersDeadline,
                result.StartDate,
                result.EndDate));
        }
        catch (EventDataExtractionException exception)
        {
            var (statusCode, errorCode) = MapError(exception.ErrorCode);
            return Error(statusCode, errorCode, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            this.logger.LogError("Event extraction failed unexpectedly for user {UserId}.", this.currentUser.UserId);
            return Error(StatusCodes.Status500InternalServerError, "extraction_failed", "Event extraction failed unexpectedly.");
        }
    }

    private static async Task<ApiRequest<ExtractEventDataRequest>?> ReadEnvelopeAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            var envelope = await request.ReadFromJsonAsync<ApiRequest<ExtractEventDataRequest>>(cancellationToken);
            return envelope?.Payload is null ? null : envelope;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (int StatusCode, string ErrorCode) MapError(EventDataExtractionErrorCode errorCode) => errorCode switch
    {
        EventDataExtractionErrorCode.InvalidSourceUrl => (StatusCodes.Status400BadRequest, "invalid_source_url"),
        EventDataExtractionErrorCode.UnsafeSourceAddress => (StatusCodes.Status400BadRequest, "unsafe_source_address"),
        EventDataExtractionErrorCode.FetchFailed => (StatusCodes.Status502BadGateway, "source_fetch_failed"),
        EventDataExtractionErrorCode.TooManyRedirects => (StatusCodes.Status502BadGateway, "too_many_redirects"),
        EventDataExtractionErrorCode.PageTooLarge => (StatusCodes.Status413PayloadTooLarge, "source_page_too_large"),
        EventDataExtractionErrorCode.InvalidModelResponse => (StatusCodes.Status502BadGateway, "invalid_model_response"),
        EventDataExtractionErrorCode.NoEventData => (StatusCodes.Status422UnprocessableEntity, "no_event_data"),
        _ => (StatusCodes.Status502BadGateway, "extraction_upstream_failed")
    };

    private static ObjectResult Error(int statusCode, string code, string message) =>
        new(new ApiErrorDto(code, message)) { StatusCode = statusCode };
}