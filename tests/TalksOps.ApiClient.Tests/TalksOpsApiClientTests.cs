using System.Net;
using System.Text;
using System.Text.Json;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Export;

namespace TalksOps.ApiClient.Tests;

public sealed class TalksOpsApiClientTests
{
    [Fact]
    public async Task FeedbackSpeakerReads_UseProposalRoutesAndCurrentUser()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse(JsonSerializer.Serialize(
            new FeedbackSettingsDto(sessionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "UTC"))));
        var client = CreateClient(handler, "speaker + 1");

        var settings = await client.GetFeedbackSettingsAsync(eventId, sessionId);
        Assert.Equal(sessionId, settings!.SessionId);
        Assert.Contains($"/api/events/{eventId}/proposals/{sessionId}/feedback/settings?", handler.RequestUri);
        Assert.Contains("userId=speaker + 1", Uri.UnescapeDataString(handler.RequestUri));
        Assert.Equal(HttpMethod.Get, handler.Method);

        var listHandler = new RecordingHandler(_ => JsonResponse("[]"));
        var feedback = await CreateClient(listHandler, "speaker").ListFeedbackAsync(eventId, sessionId);
        Assert.Empty(feedback);
        Assert.EndsWith($"/api/events/{eventId}/proposals/{sessionId}/feedback?userId=speaker", listHandler.RequestUri);
    }

    [Fact]
    public async Task SaveFeedbackSettingsAsync_UsesAuthenticatedEnvelopeAndOnlySettingsPayload()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-01-01T10:00:00+01:00");
        var handler = new RecordingHandler(_ => JsonResponse(JsonSerializer.Serialize(
            new FeedbackSettingsDto(sessionId, start, start.AddHours(1), "Europe/Rome"))));
        var result = await CreateClient(handler, "speaker").SaveFeedbackSettingsAsync(
            eventId, sessionId, new SaveFeedbackSettingsRequest(start, start.AddHours(1), "Europe/Rome"));

        Assert.Equal(start, result.ValidFrom);
        Assert.Equal("Europe/Rome", result.TimeZoneId);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.EndsWith($"/api/events/{eventId}/proposals/{sessionId}/feedback/settings", handler.RequestUri);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("speaker", json.RootElement.GetProperty("userId").GetString());
        var payload = json.RootElement.GetProperty("payload");
        Assert.Equal(["validFrom", "validTo", "timeZoneId"], payload.EnumerateObject().Select(property => property.Name));
        Assert.Equal(start, payload.GetProperty("validFrom").GetDateTimeOffset());
    }

    [Fact]
    public async Task ImportFeedbackAsync_UsesOwnedSessionEnvelopeAndPreservesExportedFields()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-01-01T10:00:00+01:00");
        var handler = new RecordingHandler(_ => JsonResponse("""{"feedbackImported":1}"""));
        var result = await CreateClient(handler, "speaker").ImportFeedbackAsync(eventId, sessionId,
            [new ImportFeedbackEntryDto(8, 9, "Useful", timestamp)]);

        Assert.Equal(1, result.FeedbackImported);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.EndsWith($"/api/events/{eventId}/proposals/{sessionId}/feedback/import", handler.RequestUri);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("speaker", json.RootElement.GetProperty("userId").GetString());
        var entry = Assert.Single(json.RootElement.GetProperty("payload").EnumerateArray());
        Assert.Equal(["sessionRating", "speakerRating", "comment", "timestamp"], entry.EnumerateObject().Select(property => property.Name));
        Assert.Equal(8, entry.GetProperty("sessionRating").GetInt32());
        Assert.Equal(9, entry.GetProperty("speakerRating").GetInt32());
        Assert.Equal("Useful", entry.GetProperty("comment").GetString());
        Assert.Equal(timestamp, entry.GetProperty("timestamp").GetDateTimeOffset());
    }

    [Fact]
    public async Task ImportFeedbackAsync_LeavesMissingTimestampForServerToAssign()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"feedbackImported":1}"""));

        await CreateClient(handler, "speaker").ImportFeedbackAsync(
            Guid.NewGuid(), Guid.NewGuid(), [new ImportFeedbackEntryDto(8, 9)]);

        using var json = JsonDocument.Parse(handler.RequestBody!);
        var entry = Assert.Single(json.RootElement.GetProperty("payload").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("timestamp").ValueKind);
    }

    [Fact]
    public async Task PublicFeedbackAsync_NeverReadsUserAndSendsOnlyParticipantFields()
    {
        var sessionId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse("""{"title":"Public title","available":true}"""));
        var client = CreateAnonymousClient(handler);
        var read = await client.GetPublicFeedbackAsync(sessionId);
        Assert.Equal("Public title", read!.Title);
        Assert.True(read.Available);
        Assert.EndsWith($"/api/feedback/{sessionId}", handler.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Null(handler.RequestBody);

        var submitHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        await CreateAnonymousClient(submitHandler).SubmitFeedbackAsync(sessionId, new SubmitFeedbackRequest(8, 9, "Clear"));
        Assert.EndsWith($"/api/feedback/{sessionId}", submitHandler.RequestUri);
        Assert.Equal(HttpMethod.Post, submitHandler.Method);
        using var json = JsonDocument.Parse(submitHandler.RequestBody!);
        Assert.Equal(["sessionRating", "speakerRating", "comment"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(8, json.RootElement.GetProperty("sessionRating").GetInt32());
        Assert.Equal(9, json.RootElement.GetProperty("speakerRating").GetInt32());
        Assert.Equal("Clear", json.RootElement.GetProperty("comment").GetString());
        Assert.DoesNotContain("userId", submitHandler.RequestUri);
    }

    [Fact]
    public async Task FeedbackReads_Map404ToNullWithoutReadingAnonymousUser()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await CreateAnonymousClient(handler).GetPublicFeedbackAsync(Guid.NewGuid()));
        Assert.Null(await CreateClient(handler, "speaker").GetFeedbackSettingsAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FeedbackSpeakerOperations_PreserveErrorCodes(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var client = CreateClient(handler, "speaker");
        var listError = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ListFeedbackAsync(Guid.NewGuid(), Guid.NewGuid()));
        var saveError = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SaveFeedbackSettingsAsync(Guid.NewGuid(), Guid.NewGuid(),
                new SaveFeedbackSettingsRequest(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "UTC")));
        var importError = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ImportFeedbackAsync(Guid.NewGuid(), Guid.NewGuid(), []));
        Assert.Equal(status, listError.StatusCode);
        Assert.Equal(status, saveError.StatusCode);
        Assert.Equal(status, importError.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetPublicFeedbackAsync_PreservesOtherErrorCodes(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateAnonymousClient(handler).GetPublicFeedbackAsync(Guid.NewGuid()));
        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SubmitFeedbackAsync_PreservesErrorStatusAndMessage(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"code":"feedback_error","message":"Feedback rejected."}""",
                Encoding.UTF8, "application/json")
        });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateAnonymousClient(handler).SubmitFeedbackAsync(Guid.NewGuid(), new SubmitFeedbackRequest(8, 9)));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("Feedback rejected.", error.Message);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_PreservesEmpty404Response()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateAnonymousClient(handler).SubmitFeedbackAsync(Guid.NewGuid(), new SubmitFeedbackRequest(8, 9)));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task FeedbackWrites_PreserveStatusWhenErrorJsonIsMalformed()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        });
        var submitError = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateAnonymousClient(handler).SubmitFeedbackAsync(Guid.NewGuid(), new SubmitFeedbackRequest(8, 9)));
        var saveError = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateClient(handler, "speaker").SaveFeedbackSettingsAsync(Guid.NewGuid(), Guid.NewGuid(),
                new SaveFeedbackSettingsRequest(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "UTC")));
        Assert.Equal(HttpStatusCode.InternalServerError, submitError.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, saveError.StatusCode);
        Assert.IsType<JsonException>(submitError.InnerException);
    }

    [Fact]
    public async Task SaveFeedbackSettingsAsync_SurfacesServerValidationMessage()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":"invalid_request","message":"Invalid feedback period."}""",
                Encoding.UTF8, "application/json")
        });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateClient(handler, "speaker").SaveFeedbackSettingsAsync(Guid.NewGuid(), Guid.NewGuid(),
                new SaveFeedbackSettingsRequest(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "UTC")));
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Invalid feedback period.", error.Message);
    }

    private static ITalksOpsApiClient CreateAnonymousClient(HttpMessageHandler handler) =>
        new TalksOpsApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://talksops.test/") },
            new UnavailableCurrentUserContext());

    private sealed class UnavailableCurrentUserContext : ICurrentUserContext
    {
        public string UserId => throw new InvalidOperationException("Anonymous operations cannot access the user.");
    }

    [Fact]
    public async Task SearchEventsAsync_AddsCurrentUserToQuery()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"items":[],"page":1,"pageSize":20,"totalCount":0}"""));
        var client = CreateClient(handler, "user + 1");

        await client.SearchEventsAsync(new EventSearchRequest(Year: 2026));

        var query = Uri.UnescapeDataString(new Uri(handler.RequestUri).Query);
        Assert.Contains("userId=user + 1", query);
        Assert.Contains("year=2026", query);
    }

    [Fact]
    public async Task CreateEventAsync_WrapsRequestWithCurrentUser()
    {
        const string responseBody = """{"id":"00000000-0000-0000-0000-000000000001","ownerId":"user-1","name":"Summit","location":"Rome","startDate":"2026-05-01","endDate":"2026-05-02","costs":{}}""";
        var handler = new RecordingHandler(_ => JsonResponse(responseBody));
        var client = CreateClient(handler, "user-1");

        await client.CreateEventAsync(new SaveEventRequest(
            "Summit", "Rome", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2),
            new Dictionary<string, decimal>()));

        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("user-1", request.RootElement.GetProperty("userId").GetString());
        Assert.Equal("Summit", request.RootElement.GetProperty("payload").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExportEventsAsync_SendsOptionalDatesAndUser()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"schemaVersion":1,"events":[]}"""));
        var client = CreateClient(handler, "user-1");

        await client.ExportEventsAsync(new DateOnly(2026, 1, 1), null);

        var query = Uri.UnescapeDataString(new Uri(handler.RequestUri).Query);
        Assert.Contains("from=2026-01-01", query);
        Assert.DoesNotContain("to=", query);
        Assert.Contains("userId=user-1", query);
    }

    [Fact]
    public async Task ExportEventsAsync_DeserializesFeedbackAndSettings()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "schemaVersion": 1,
              "events": [{
                "name": "Conference",
                "location": "Milan",
                "startDate": "2026-10-20",
                "endDate": "2026-10-21",
                "sessions": [{
                  "title": "Talk",
                  "feedbackSettings": {
                    "validFrom": "2026-10-20T08:00:00Z",
                    "validTo": "2026-10-22T08:00:00Z",
                    "timeZoneId": "Europe/Rome"
                  },
                  "feedback": [{
                    "sessionRating": 8,
                    "speakerRating": 9,
                    "comment": "Useful",
                    "timestamp": "2026-10-20T10:30:00Z"
                  }]
                }]
              }]
            }
            """));
        var client = CreateClient(handler, "user-1");

        var document = await client.ExportEventsAsync(null, null);

        var session = Assert.Single(Assert.Single(document.Events).Sessions);
        Assert.Equal("Europe/Rome", session.FeedbackSettings!.TimeZoneId);
        Assert.Equal(8, Assert.Single(session.Feedback!).SessionRating);
    }

    [Fact]
    public async Task ImportEventsAsync_SendsFeedbackAndSettingsInExistingPayload()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"eventsImported":1,"sessionsImported":1,"feedbackImported":1}"""));
        var client = CreateClient(handler, "user-1");
        var document = new EventsExportDocument
        {
            Events =
            [
                new ExportedEvent
                {
                    Name = "Conference", Location = "Milan",
                    StartDate = new DateOnly(2026, 10, 20), EndDate = new DateOnly(2026, 10, 21),
                    Sessions =
                    [
                        new ExportedSession
                        {
                            Title = "Talk",
                            FeedbackSettings = new ExportedFeedbackSettings
                            {
                                ValidFrom = DateTimeOffset.Parse("2026-10-20T08:00:00Z"),
                                ValidTo = DateTimeOffset.Parse("2026-10-22T08:00:00Z"),
                                TimeZoneId = "Europe/Rome"
                            },
                            Feedback =
                            [
                                new ExportedFeedback
                                {
                                    SessionRating = 8, SpeakerRating = 9,
                                    Comment = "Useful",
                                    Timestamp = DateTimeOffset.Parse("2026-10-20T10:30:00Z")
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var result = await client.ImportEventsAsync(document);

        Assert.Equal(1, result.FeedbackImported);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        var payloadSession = request.RootElement.GetProperty("payload").GetProperty("events")[0]
            .GetProperty("sessions")[0];
        Assert.Equal("Europe/Rome", payloadSession.GetProperty("feedbackSettings").GetProperty("timeZoneId").GetString());
        var feedback = payloadSession.GetProperty("feedback")[0];
        Assert.Equal(8, feedback.GetProperty("sessionRating").GetInt32());
        Assert.False(feedback.TryGetProperty("id", out _));
        Assert.False(feedback.TryGetProperty("metadata", out _));
    }

    [Fact]
    public async Task ImportEventsAsync_SurfacesServerValidationMessage()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Event #1: An event name is required.")
        });
        var client = CreateClient(handler, "user-1");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ImportEventsAsync(new EventsExportDocument()));

        Assert.Contains("event name is required", exception.Message);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("user-1", request.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task ExtractEventDataAsync_WrapsRequestAndReturnsNullableFields()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"name":"Summit","location":null,"officialWebsiteUrl":null,"callForPapersUrl":"https://example.com/cfp","callForPapersDeadline":null,"startDate":"01/05/2026","endDate":null}"""));
        var client = CreateClient(handler, "user-1");

        var result = await client.ExtractEventDataAsync(new ExtractEventDataRequest("https://example.com/cfp"));

        Assert.Equal("Summit", result.Name);
        Assert.Null(result.Location);
        Assert.Equal("https://example.com/cfp", result.CallForPapersUrl);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("user-1", request.RootElement.GetProperty("userId").GetString());
        Assert.Equal(
            "https://example.com/cfp",
            request.RootElement.GetProperty("payload").GetProperty("sourcePageUrl").GetString());
    }

    [Fact]
    public async Task ExtractEventDataAsync_SurfacesStructuredServerError()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":"invalid_url","message":"The page URL is invalid."}""")
        });
        var client = CreateClient(handler, "user-1");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ExtractEventDataAsync(new ExtractEventDataRequest("not a URL")));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("The page URL is invalid.", exception.Message);
    }

    [Fact]
    public async Task ExtractEventDataAsync_PreservesRateLimitStatusAndMessage()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"code":"rate_limited","message":"Please wait before retrying."}""")
        });
        var client = CreateClient(handler, "user-1");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ExtractEventDataAsync(new ExtractEventDataRequest("https://example.com/cfp")));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal("Please wait before retrying.", exception.Message);
    }

    private static ITalksOpsApiClient CreateClient(HttpMessageHandler handler, string userId)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://talksops.test/")
        };
        return new TalksOpsApiClient(httpClient, new StubCurrentUserContext(userId));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubCurrentUserContext(string userId) : ICurrentUserContext
    {
        public string UserId { get; } = userId;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public string RequestUri { get; private set; } = string.Empty;

        public string? RequestBody { get; private set; }

        public HttpMethod? Method { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestUri = request.RequestUri!.ToString();
            this.Method = request.Method;
            if (request.Content is not null)
            {
                this.RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responseFactory(request);
        }
    }
}