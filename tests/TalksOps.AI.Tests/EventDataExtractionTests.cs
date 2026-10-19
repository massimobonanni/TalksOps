using Microsoft.Extensions.Logging.Abstractions;
using TalksOps.AI;

namespace TalksOps.AI.Tests;

public sealed class EventDataExtractionTests
{
    [Fact]
    public async Task ExtractAsync_ReplacesPromptPlaceholders_AndNormalizesExactDates()
    {
        var pageFetcher = new StubPageFetcher("<html>Conference content</html>");
        var modelClient = new StubChatClient(
            """
            {
              "name": "  Example Conf ",
              "location": "Online",
              "officialWebsiteUrl": "https://example.org",
              "callForPapersUrl": null,
              "callForPapersDeadline": "31/02/2027",
              "startDate": "04/05/2027",
              "endDate": "06/05/2027"
            }
            """);
        var service = new EventDataExtractionService(
            pageFetcher,
            modelClient,
            NullLogger<EventDataExtractionService>.Instance);

        var result = await service.ExtractAsync("https://example.org/cfp", "user-123");

        Assert.Equal("Example Conf", result.Name);
        Assert.Null(result.CallForPapersDeadline);
        Assert.Equal("04/05/2027", result.StartDate);
        Assert.Equal("06/05/2027", result.EndDate);
        Assert.Contains("https://example.org/cfp", modelClient.Prompt);
        Assert.Contains("<html>Conference content</html>", modelClient.Prompt);
        Assert.DoesNotContain("[SOURCE_PAGE_URL]", modelClient.Prompt);
        Assert.DoesNotContain("[HTML_CONTENT]", modelClient.Prompt);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ExtractAsync_RejectsMalformedOrNullModelOutput(string content)
    {
        var service = CreateService(content);

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => service.ExtractAsync("https://example.org/cfp", "user-123"));

        Assert.Equal(EventDataExtractionErrorCode.InvalidModelResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_RejectsResultsWithoutExtractableValues()
    {
        var service = CreateService(
            """{"name":null,"location":null,"officialWebsiteUrl":null,"callForPapersUrl":null,"callForPapersDeadline":null,"startDate":null,"endDate":null}""");

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => service.ExtractAsync("https://example.org/cfp", "user-123"));

        Assert.Equal(EventDataExtractionErrorCode.NoEventData, exception.ErrorCode);
    }

    private static EventDataExtractionService CreateService(string content) =>
        new(
            new StubPageFetcher("<html></html>"),
            new StubChatClient(content),
            NullLogger<EventDataExtractionService>.Instance);

    private sealed class StubPageFetcher(string html) : IEventPageFetcher
    {
        public Task<string> FetchAsync(string sourcePageUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(html);
    }

    private sealed class StubChatClient(string content) : IEventDataChatClient
    {
        public string DeploymentName => "test-deployment";

        public string Prompt { get; private set; } = string.Empty;

        public Task<EventDataModelResponse> CompleteAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            this.Prompt = prompt;
            return Task.FromResult(new EventDataModelResponse(content, this.DeploymentName, 125, 42));
        }
    }
}