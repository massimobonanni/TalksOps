using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TalksOps.AI;
using TalksOps.ApiClient.Contracts;
using TalksOps.Functions;

namespace TalksOps.Functions.Tests;

public sealed class EventDataExtractionFunctionTests
{
    [Fact]
    public async Task ExtractEventData_ReturnsExtractedDataAndUsesEnvelopeUser()
    {
        using var limiter = CreateRateLimiter();
        var service = new StubExtractionService();
        var function = CreateFunction(service, limiter);

        var result = await function.ExtractEventData(CreateRequest("user-123", "https://example.org/cfp"), default);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode ?? StatusCodes.Status200OK);
        Assert.Equal("user-123", service.UserId);
        Assert.Equal("Example Conference", Assert.IsType<ExtractEventDataResultDto>(response.Value).Name);
    }

    [Theory]
    [InlineData(EventDataExtractionErrorCode.InvalidSourceUrl, StatusCodes.Status400BadRequest, "invalid_source_url")]
    [InlineData(EventDataExtractionErrorCode.UnsafeSourceAddress, StatusCodes.Status400BadRequest, "unsafe_source_address")]
    [InlineData(EventDataExtractionErrorCode.FetchFailed, StatusCodes.Status502BadGateway, "source_fetch_failed")]
    [InlineData(EventDataExtractionErrorCode.TooManyRedirects, StatusCodes.Status502BadGateway, "too_many_redirects")]
    [InlineData(EventDataExtractionErrorCode.PageTooLarge, StatusCodes.Status413PayloadTooLarge, "source_page_too_large")]
    [InlineData(EventDataExtractionErrorCode.InvalidModelResponse, StatusCodes.Status502BadGateway, "invalid_model_response")]
    [InlineData(EventDataExtractionErrorCode.NoEventData, StatusCodes.Status422UnprocessableEntity, "no_event_data")]
    public async Task ExtractEventData_MapsExtractionErrors(
        EventDataExtractionErrorCode errorCode,
        int expectedStatus,
        string expectedCode)
    {
        using var limiter = CreateRateLimiter();
        var function = CreateFunction(new StubExtractionService(errorCode), limiter);

        var result = await function.ExtractEventData(CreateRequest("user-123", "https://example.org/cfp"), default);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, response.StatusCode);
        var error = Assert.IsType<ApiErrorDto>(response.Value);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("Safe extraction error.", error.Message);
    }

    [Fact]
    public async Task ExtractEventData_LimitsEachUserToOneRequestPerWindow()
    {
        using var limiter = CreateRateLimiter();
        var service = new StubExtractionService();
        var function = CreateFunction(service, limiter);

        var first = await function.ExtractEventData(CreateRequest("user-1", "https://example.org/cfp"), default);
        var limitedRequest = CreateRequest("user-1", "https://example.org/cfp");
        var limited = await function.ExtractEventData(limitedRequest, default);
        var otherUser = await function.ExtractEventData(CreateRequest("user-2", "https://example.org/cfp"), default);

        Assert.IsType<OkObjectResult>(first);
        var rateLimitResponse = Assert.IsType<ObjectResult>(limited);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rateLimitResponse.StatusCode);
        Assert.Equal("rate_limited", Assert.IsType<ApiErrorDto>(rateLimitResponse.Value).Code);
        Assert.Equal("60", limitedRequest.HttpContext.Response.Headers.RetryAfter);
        Assert.IsType<OkObjectResult>(otherUser);
        Assert.Equal(2, service.CallCount);
    }

    [Fact]
    public async Task ExtractEventData_ReturnsStructuredBadRequestForMalformedJson()
    {
        using var limiter = CreateRateLimiter();
        var function = CreateFunction(new StubExtractionService(), limiter);
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{"));

        var result = await function.ExtractEventData(request, default);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", Assert.IsType<ApiErrorDto>(response.Value).Code);
    }

    [Fact]
    public async Task ExtractEventData_ReturnsServiceUnavailableWhenFoundryIsNotConfigured()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<RequestCurrentUserContext>()
            .AddSingleton<PartitionedRateLimiter<string>>(_ => CreateRateLimiter())
            .BuildServiceProvider();
        var function = ActivatorUtilities.CreateInstance<EventDataExtractionFunction>(services);

        var result = await function.ExtractEventData(CreateRequest("user-123", "https://example.org/cfp"), default);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("extraction_unavailable", Assert.IsType<ApiErrorDto>(response.Value).Code);
    }

    private static EventDataExtractionFunction CreateFunction(
        IEventDataExtractionService? service,
        PartitionedRateLimiter<string> limiter) =>
        new(new RequestCurrentUserContext(), limiter, NullLogger<EventDataExtractionFunction>.Instance, service);

    private static HttpRequest CreateRequest(string userId, string sourcePageUrl)
    {
        var request = new DefaultHttpContext();
        request.Request.ContentType = "application/json";
        request.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
            new ApiRequest<ExtractEventDataRequest>(userId, new ExtractEventDataRequest(sourcePageUrl))));
        return request.Request;
    }

    private static PartitionedRateLimiter<string> CreateRateLimiter() =>
        PartitionedRateLimiter.Create<string, string>(userId =>
            RateLimitPartition.GetFixedWindowLimiter(
                userId,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

    private sealed class StubExtractionService(EventDataExtractionErrorCode? errorCode = null) : IEventDataExtractionService
    {
        public string? UserId { get; private set; }
        public int CallCount { get; private set; }

        public Task<ExtractedEventData> ExtractAsync(
            string sourcePageUrl,
            string userId,
            CancellationToken cancellationToken = default)
        {
            this.UserId = userId;
            this.CallCount++;
            if (errorCode is { } code)
            {
                throw new EventDataExtractionException(code, "Safe extraction error.");
            }

            return Task.FromResult(new ExtractedEventData(
                "Example Conference", "Online", null, sourcePageUrl, null, null, null));
        }
    }
}