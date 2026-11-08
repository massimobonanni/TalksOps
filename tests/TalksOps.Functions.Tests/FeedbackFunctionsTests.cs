using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Functions;

namespace TalksOps.Functions.Tests;

public sealed class FeedbackFunctionsTests
{
    [Theory]
    [InlineData(nameof(FeedbackFunctions.GetFeedbackSettings), AuthorizationLevel.Function, "get",
        "events/{eventId}/proposals/{proposalId}/feedback/settings")]
    [InlineData(nameof(FeedbackFunctions.SaveFeedbackSettings), AuthorizationLevel.Function, "put",
        "events/{eventId}/proposals/{proposalId}/feedback/settings")]
    [InlineData(nameof(FeedbackFunctions.ListFeedback), AuthorizationLevel.Function, "get",
        "events/{eventId}/proposals/{proposalId}/feedback")]
    [InlineData(nameof(FeedbackFunctions.GetPublicFeedback), AuthorizationLevel.Anonymous, "get", "feedback/{sessionId}")]
    [InlineData(nameof(FeedbackFunctions.SubmitFeedback), AuthorizationLevel.Anonymous, "post", "feedback/{sessionId}")]
    public void Triggers_DeclareExactRoutesAndAuthorization(
        string method, AuthorizationLevel level, string verb, string route)
    {
        var trigger = typeof(FeedbackFunctions).GetMethod(method)!.GetParameters()[0]
            .GetCustomAttribute<HttpTriggerAttribute>()!;
        Assert.Equal(level, trigger.AuthLevel);
        Assert.Equal(route, trigger.Route);
        Assert.NotNull(trigger.Methods);
        Assert.Equal([verb], trigger.Methods);
    }

    [Theory]
    [InlineData("other-user", true, true)]
    [InlineData("owner", false, true)]
    [InlineData("owner", true, false)]
    public async Task SpeakerOperations_RejectNonOwnershipOrMissingSession(
        string user, bool eventExists, bool sessionExists)
    {
        using var fixture = new Fixture();
        fixture.Owned.EventExists = eventExists;
        fixture.Owned.SessionExists = sessionExists;
        var function = fixture.Function;

        Assert.IsType<NotFoundResult>(await function.GetFeedbackSettings(
            Request(user: user), fixture.EventId, fixture.SessionId, default));
        Assert.IsType<NotFoundResult>(await function.ListFeedback(
            Request(user: user), fixture.EventId, fixture.SessionId, default));
        Assert.IsType<NotFoundResult>(await function.SaveFeedbackSettings(
            Request(SettingsBody(user)), fixture.EventId, fixture.SessionId, default));
        Assert.Equal(0, fixture.Feedback.SettingsReads);
        Assert.Equal(0, fixture.Feedback.ListCalls);
        Assert.Null(fixture.Feedback.Saved);
    }

    [Fact]
    public async Task SpeakerOperations_StayAvailableOutsideWindowAndUseVerifiedIds()
    {
        using var fixture = new Fixture();
        fixture.Clock.Now = fixture.Feedback.Settings!.ValidTo.AddDays(1);
        var result = await fixture.Function.SaveFeedbackSettings(
            Request(SettingsBody("owner")), fixture.EventId, fixture.SessionId, default);

        var saved = Assert.IsType<FeedbackSettingsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(Guid.Parse(fixture.SessionId), saved.SessionId);
        Assert.Equal(Guid.Parse(fixture.EventId), fixture.Feedback.Saved!.EventId);
        Assert.IsType<OkObjectResult>(await fixture.Function.GetFeedbackSettings(
            Request(user: "owner"), fixture.EventId, fixture.SessionId, default));
        var list = Assert.IsType<OkObjectResult>(await fixture.Function.ListFeedback(
            Request(user: "owner"), fixture.EventId, fixture.SessionId, default));
        var value = Assert.Single(Assert.IsType<SessionFeedbackDto[]>(list.Value));
        Assert.Equal("192.0.2.1", value.Metadata.IpAddress);
        Assert.Equal("owner", fixture.Owned.LastUser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("""{"userId":"owner","payload":null}""")]
    [InlineData("""{"userId":"","payload":{"validFrom":"2026-01-01T00:00:00Z","validTo":"2026-01-02T00:00:00Z","timeZoneId":"UTC"}}""")]
    [InlineData("""{"userId":"owner","payload":{"validFrom":"2026-01-02T00:00:00Z","validTo":"2026-01-01T00:00:00Z","timeZoneId":"UTC"}}""")]
    [InlineData("""{"userId":"owner","payload":{"validFrom":"2026-01-01T00:00:00Z","validTo":"2026-01-02T00:00:00Z","timeZoneId":""}}""")]
    public async Task SaveSettings_RejectsMalformedEnvelopeAndInvalidPeriod(string? body)
    {
        using var fixture = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await fixture.Function.SaveFeedbackSettings(
            Request(body), fixture.EventId, fixture.SessionId, default));
        Assert.Null(fixture.Feedback.Saved);
    }

    [Fact]
    public async Task SpeakerReads_RequireUserAndValidIds()
    {
        using var fixture = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await fixture.Function.GetFeedbackSettings(
            Request(), fixture.EventId, fixture.SessionId, default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Function.ListFeedback(
            Request(), fixture.EventId, fixture.SessionId, default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Function.GetFeedbackSettings(
            Request(user: "owner"), "invalid", fixture.SessionId, default));
        Assert.IsType<BadRequestObjectResult>(await fixture.Function.SaveFeedbackSettings(
            Request(SettingsBody("owner")), fixture.EventId, Guid.Empty.ToString(), default));
    }

    [Fact]
    public async Task SpeakerOperations_RejectSessionUnderAnotherEvent()
    {
        using var fixture = new Fixture();
        var otherEvent = Guid.NewGuid().ToString();
        Assert.IsType<NotFoundResult>(await fixture.Function.GetFeedbackSettings(
            Request(user: "owner"), otherEvent, fixture.SessionId, default));
        Assert.IsType<NotFoundResult>(await fixture.Function.SaveFeedbackSettings(
            Request(SettingsBody("owner")), otherEvent, fixture.SessionId, default));
        Assert.IsType<NotFoundResult>(await fixture.Function.ListFeedback(
            Request(user: "owner"), fixture.EventId, Guid.NewGuid().ToString(), default));
        Assert.Equal(0, fixture.Feedback.SettingsReads);
        Assert.Null(fixture.Feedback.Saved);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(60, false)]
    [InlineData(61, false)]
    public async Task PublicOperations_EnforceInclusiveStartAndExclusiveEnd(int seconds, bool available)
    {
        using var fixture = new Fixture();
        fixture.Clock.Now = fixture.Feedback.Settings!.ValidFrom.AddSeconds(seconds);
        var readRequest = Request(user: "ignored-user");
        var read = await fixture.Function.GetPublicFeedback(readRequest, fixture.SessionId, default);
        var write = await fixture.Function.SubmitFeedback(
            Request("""{"sessionRating":1,"speakerRating":10}""", user: "ignored-user"), fixture.SessionId, default);
        if (available)
        {
            var value = Assert.IsType<PublicFeedbackDto>(Assert.IsType<OkObjectResult>(read).Value);
            Assert.True(value.Available);
            Assert.Equal("Session title", value.Title);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(["title", "available"], json.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal(StatusCodes.Status201Created, Assert.IsType<StatusCodeResult>(write).StatusCode);
        }
        else
        {
            Assert.IsType<NotFoundResult>(read);
            Assert.IsType<NotFoundResult>(write);
            Assert.Null(fixture.Feedback.Submission);
        }

        Assert.Equal("no-store", readRequest.HttpContext.Response.Headers.CacheControl);
        Assert.Throws<InvalidOperationException>(() => fixture.CurrentUser.UserId);
        Assert.Null(fixture.Owned.LastUser);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicOperations_HideMissingSettingsOrSession(bool missingSettings)
    {
        using var fixture = new Fixture();
        if (missingSettings)
        {
            fixture.Feedback.Settings = null;
        }
        else
        {
            fixture.Feedback.Title = null;
        }

        Assert.IsType<NotFoundResult>(await fixture.Function.GetPublicFeedback(Request(), fixture.SessionId, default));
        Assert.IsType<NotFoundResult>(await fixture.Function.SubmitFeedback(
            Request("""{"sessionRating":8,"speakerRating":9}"""), fixture.SessionId, default));
        Assert.Null(fixture.Feedback.Submission);
    }

    [Fact]
    public async Task SubmitFeedback_IgnoresSpoofedMetadataIdentityTimestampAndId()
    {
        using var fixture = new Fixture();
        var request = Request("""
            {"sessionRating":8,"speakerRating":9,"comment":"Useful","userId":"spoofed",
             "ipAddress":"spoofed","userAgent":"spoofed","id":"spoofed","timestamp":"1900-01-01",
             "metadata":{"ipAddress":"spoofed","userAgent":"spoofed"}}
            """);
        request.Headers.UserAgent = "Real browser";
        request.Headers["X-Forwarded-For"] = "203.0.113.1";
        request.Headers["Forwarded"] = "for=203.0.113.2";

        var result = await fixture.Function.SubmitFeedback(request, fixture.SessionId, default);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(8, fixture.Feedback.Submission!.SessionRating);
        Assert.Equal(9, fixture.Feedback.Submission.SpeakerRating);
        Assert.Equal("Useful", fixture.Feedback.Submission.Comment);
        Assert.Equal("192.0.2.1", fixture.Feedback.Metadata!.IpAddress);
        Assert.Equal("Real browser", fixture.Feedback.Metadata.UserAgent);
        Assert.Throws<InvalidOperationException>(() => fixture.CurrentUser.UserId);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"sessionRating":8}""")]
    [InlineData("""{"sessionRating":0,"speakerRating":9}""")]
    [InlineData("""{"sessionRating":8,"speakerRating":11}""")]
    [InlineData("""{"sessionRating":"eight","speakerRating":9}""")]
    [InlineData("""{"userId":"owner","payload":{"sessionRating":8,"speakerRating":9}}""")]
    public async Task SubmitFeedback_Returns400ForInvalidPayload(string body)
    {
        using var fixture = new Fixture();
        var result = await fixture.Function.SubmitFeedback(Request(body), fixture.SessionId, default);
        var error = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_request", Assert.IsType<ApiErrorDto>(error.Value).Code);
        Assert.Null(fixture.Feedback.Submission);
    }

    [Theory]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public async Task SubmitFeedback_EnforcesExactCommentLimit(int length, bool accepted)
    {
        using var fixture = new Fixture();
        var body = JsonSerializer.Serialize(new SubmitFeedbackRequest(8, 9, new string('a', length)));
        var result = await fixture.Function.SubmitFeedback(Request(body), fixture.SessionId, default);
        if (accepted)
        {
            Assert.Equal(201, Assert.IsType<StatusCodeResult>(result).StatusCode);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Null(fixture.Feedback.Submission);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubmitFeedback_LimitsBodyEvenWithoutContentLength(bool withLength)
    {
        using var fixture = new Fixture();
        var request = Request(new string(' ', FeedbackFunctions.MaxRequestBytes + 1));
        request.ContentLength = withLength ? FeedbackFunctions.MaxRequestBytes + 1 : null;
        var result = Assert.IsType<ObjectResult>(await fixture.Function.SubmitFeedback(request, fixture.SessionId, default));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
        Assert.Null(fixture.Feedback.Submission);
    }

    [Fact]
    public async Task SubmitFeedback_AcceptsExactlyMaximumBodySize()
    {
        using var fixture = new Fixture();
        const string json = """{"sessionRating":8,"speakerRating":9}""";
        var request = Request(json.PadRight(FeedbackFunctions.MaxRequestBytes));
        request.ContentLength = FeedbackFunctions.MaxRequestBytes;
        var result = await fixture.Function.SubmitFeedback(request, fixture.SessionId, default);
        Assert.Equal(201, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Null(fixture.Feedback.Submission!.Comment);
    }

    [Fact]
    public async Task PublicOperations_InvalidSessionIdsReturnIndistinguishable404()
    {
        using var fixture = new Fixture();
        Assert.IsType<NotFoundResult>(await fixture.Function.GetPublicFeedback(Request(), "invalid", default));
        Assert.IsType<NotFoundResult>(await fixture.Function.SubmitFeedback(
            Request("""{"sessionRating":8,"speakerRating":9}"""), Guid.Empty.ToString(), default));
        Assert.Equal(0, fixture.Feedback.SettingsReads);
    }

    [Fact]
    public async Task SpeakerReads_MissingSettingsReturn404ButListRemainsAccessible()
    {
        using var fixture = new Fixture();
        fixture.Feedback.Settings = null;
        Assert.IsType<NotFoundResult>(await fixture.Function.GetFeedbackSettings(
            Request(user: "owner"), fixture.EventId, fixture.SessionId, default));
        Assert.IsType<OkObjectResult>(await fixture.Function.ListFeedback(
            Request(user: "owner"), fixture.EventId, fixture.SessionId, default));
    }

    [Fact]
    public async Task SubmitFeedback_AfterSuccessfulGetRechecksExpiredOrRemovedSettings()
    {
        using var fixture = new Fixture();
        Assert.IsType<OkObjectResult>(await fixture.Function.GetPublicFeedback(Request(), fixture.SessionId, default));
        var readsAfterGet = fixture.Feedback.SettingsReads;
        fixture.Clock.Now = fixture.Feedback.Settings!.ValidTo;

        Assert.IsType<NotFoundResult>(await fixture.Function.SubmitFeedback(
            Request("""{"sessionRating":8,"speakerRating":9}"""), fixture.SessionId, default));
        Assert.True(fixture.Feedback.SettingsReads > readsAfterGet);
        Assert.Null(fixture.Feedback.Submission);

        fixture.Feedback.Settings = null;
        Assert.IsType<NotFoundResult>(await fixture.Function.SubmitFeedback(
            Request("""{"sessionRating":8,"speakerRating":9}"""), fixture.SessionId, default));
        Assert.Null(fixture.Feedback.Submission);
        Assert.Throws<InvalidOperationException>(() => fixture.CurrentUser.UserId);
        Assert.Null(fixture.Owned.LastUser);
    }

    [Fact]
    public async Task SubmitFeedback_RechecksWindowAfterBodyRead()
    {
        using var fixture = new Fixture();
        var request = Request();
        request.Body = new AdvancingStream(
            Encoding.UTF8.GetBytes("""{"sessionRating":8,"speakerRating":9}"""),
            () => fixture.Clock.Now = fixture.Feedback.Settings!.ValidTo);

        Assert.IsType<NotFoundResult>(await fixture.Function.SubmitFeedback(request, fixture.SessionId, default));
        Assert.Null(fixture.Feedback.Submission);
    }

    [Fact]
    public async Task PublicOperations_ShareIpRateLimitAndIgnoreSpoofedForwarding()
    {
        using var fixture = new Fixture(1);
        Assert.IsType<OkObjectResult>(await fixture.Function.GetPublicFeedback(Request(), fixture.SessionId, default));
        var request = Request("""{"sessionRating":8,"speakerRating":9}""");
        request.Headers["X-Forwarded-For"] = "203.0.113.1";
        var limited = Assert.IsType<ObjectResult>(
            await fixture.Function.SubmitFeedback(request, fixture.SessionId, default));
        Assert.Equal(StatusCodes.Status429TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", Assert.IsType<ApiErrorDto>(limited.Value).Code);
        Assert.True(int.Parse(request.HttpContext.Response.Headers.RetryAfter.ToString()) > 0);
        Assert.Null(fixture.Feedback.Submission);
        var limitedGet = Assert.IsType<ObjectResult>(
            await fixture.Function.GetPublicFeedback(Request(), fixture.SessionId, default));
        Assert.Equal(StatusCodes.Status429TooManyRequests, limitedGet.StatusCode);
        var otherIp = Request();
        otherIp.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.2");
        Assert.IsType<OkObjectResult>(await fixture.Function.GetPublicFeedback(otherIp, fixture.SessionId, default));
    }

    private static string SettingsBody(string user) => JsonSerializer.Serialize(
        new ApiRequest<SaveFeedbackSettingsRequest>(user,
            new SaveFeedbackSettingsRequest(DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-01-01T00:01:00Z"), "UTC")));

    private static HttpRequest Request(string? body = null, string? user = null)
    {
        var request = new DefaultHttpContext().Request;
        request.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        request.ContentType = "application/json";
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        if (user is not null)
        {
            request.QueryString = new QueryString($"?userId={user}");
        }

        return request;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PartitionedRateLimiter<string> limiter;
        public string EventId { get; } = Guid.NewGuid().ToString();
        public string SessionId { get; } = Guid.NewGuid().ToString();
        public RequestCurrentUserContext CurrentUser { get; } = new();
        public TestClock Clock { get; } = new();
        public StubFeedbackService Feedback { get; }
        public OwnedServices Owned { get; }
        public FeedbackFunctions Function { get; }

        public Fixture(int permitLimit = 100)
        {
            this.limiter = PartitionedRateLimiter.Create<string, string>(ip =>
                RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
            this.Feedback = new StubFeedbackService(Guid.Parse(this.SessionId), Guid.Parse(this.EventId), this.Clock.Now);
            this.Owned = new OwnedServices(this.CurrentUser, Guid.Parse(this.EventId), Guid.Parse(this.SessionId));
            this.Function = new FeedbackFunctions(this.Feedback, this.Owned, this.Owned,
                this.CurrentUser, this.Clock, this.limiter);
        }

        public void Dispose() => this.limiter.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    private sealed class AdvancingStream(byte[] bytes, Action advance) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            advance();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class StubFeedbackService(Guid sessionId, Guid eventId, DateTimeOffset from) : IFeedbackService
    {
        public FeedbackSettings? Settings { get; set; } = new()
        {
            SessionId = sessionId, EventId = eventId, ValidFrom = from, ValidTo = from.AddMinutes(1), TimeZoneId = "UTC"
        };
        public string? Title { get; set; } = "Session title";
        public FeedbackSettings? Saved { get; private set; }
        public SessionFeedbackSubmission? Submission { get; private set; }
        public SessionFeedbackMetadata? Metadata { get; private set; }
        public int SettingsReads { get; private set; }
        public int ListCalls { get; private set; }

        public Task<string?> GetSessionTitleAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Assert.Equal(sessionId, id);
            return Task.FromResult(this.Title);
        }

        public Task<FeedbackSettings?> GetSettingsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Assert.Equal(sessionId, id);
            this.SettingsReads++;
            return Task.FromResult(this.Settings);
        }

        public Task<FeedbackSettings> SaveSettingsAsync(FeedbackSettings settings, CancellationToken cancellationToken = default)
        {
            this.Saved = settings;
            return Task.FromResult(settings);
        }

        public Task<IReadOnlyList<SessionFeedback>> ListBySessionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            this.ListCalls++;
            return Task.FromResult<IReadOnlyList<SessionFeedback>>([new SessionFeedback
            {
                SessionId = id, SessionRating = 8, SpeakerRating = 9,
                Metadata = new SessionFeedbackMetadata { IpAddress = "192.0.2.1" }
            }]);
        }

        public Task<SessionFeedback> CreateAsync(Guid id, SessionFeedbackSubmission submission,
            SessionFeedbackMetadata metadata, CancellationToken cancellationToken = default)
        {
            Assert.Equal(sessionId, id);
            this.Submission = submission;
            this.Metadata = metadata;
            return Task.FromResult(new SessionFeedback
            {
                SessionId = id, SessionRating = submission.SessionRating, SpeakerRating = submission.SpeakerRating,
                Metadata = metadata
            });
        }

        public Task ImportSessionAsync(
            Guid id,
            FeedbackSettings? settings,
            IReadOnlyList<SessionFeedbackImport> feedback,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class OwnedServices(RequestCurrentUserContext user, Guid eventId, Guid sessionId)
        : IEventService, ISessionProposalService
    {
        public bool EventExists { get; set; } = true;
        public bool SessionExists { get; set; } = true;
        public string? LastUser { get; private set; }

        public Task<Event?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            this.LastUser = user.UserId;
            return Task.FromResult<Event?>(this.EventExists && id == eventId && user.UserId == "owner"
                ? new Event
                {
                    Id = id, OwnerId = "owner", Name = "Event", Location = "Online",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 2)
                } : null);
        }

        public Task<SessionProposal?> GetAsync(Guid parentId, Guid id, CancellationToken cancellationToken = default)
        {
            this.LastUser = user.UserId;
            return Task.FromResult<SessionProposal?>(
                this.SessionExists && parentId == eventId && id == sessionId && user.UserId == "owner"
                    ? new SessionProposal { Id = id, EventId = parentId, OwnerId = "owner", Title = "Session title" }
                    : null);
        }

        public Task<PagedResult<Event>> SearchAsync(EventSearchCriteria criteria, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionProposal>> ListByEventAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionProposal> CreateAsync(SessionProposal value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionProposal?> UpdateAsync(SessionProposal value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionProposal?> ChangeStatusAsync(Guid parentId, Guid id, ProposalStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionProposal?> CancelAsync(Guid parentId, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid parentId, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
