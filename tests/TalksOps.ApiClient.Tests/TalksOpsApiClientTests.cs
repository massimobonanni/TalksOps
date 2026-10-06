using System.Net;
using System.Text;
using System.Text.Json;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;

namespace TalksOps.ApiClient.Tests;

public sealed class TalksOpsApiClientTests
{
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestUri = request.RequestUri!.ToString();
            if (request.Content is not null)
            {
                this.RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responseFactory(request);
        }
    }
}