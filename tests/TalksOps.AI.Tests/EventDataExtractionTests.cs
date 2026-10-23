using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TalksOps.AI;
using TalksOps.AI.Providers;
using TalksOps.AI.Services;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Extraction;

namespace TalksOps.AI.Tests;

public sealed class EventDataExtractionTests
{
    [Fact]
    public async Task ExtractAsync_ReplacesPromptPlaceholders_AndNormalizesExactDates()
    {
        var pageFetcher = new StubPageFetcher("<html>Conference content</html>");
        var tokensConsumptionRepository = new StubTokensConsumptionRepository();
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
            new AngleSharpEventHtmlCleaner(),
            modelClient,
            tokensConsumptionRepository,
            NullLogger<EventDataExtractionService>.Instance);

        var extractionStartedAt = DateTimeOffset.UtcNow;
        var result = await service.ExtractAsync("https://example.org/cfp", "user-123");

        Assert.Equal("Example Conf", result.Name);
        Assert.Null(result.CallForPapersDeadline);
        Assert.Equal("04/05/2027", result.StartDate);
        Assert.Equal("06/05/2027", result.EndDate);
        Assert.Contains("https://example.org/cfp", modelClient.Prompt);
        Assert.Contains("Conference content", modelClient.Prompt);
        Assert.DoesNotContain("<html>", modelClient.Prompt);
        Assert.DoesNotContain("[SOURCE_PAGE_URL]", modelClient.Prompt);
        Assert.DoesNotContain("[HTML_CONTENT]", modelClient.Prompt);

        var consumption = Assert.Single(tokensConsumptionRepository.Values);
        Assert.Equal("user-123", consumption.UserId);
        Assert.Equal("test-deployment", consumption.DeploymentName);
        Assert.Equal(125, consumption.InputTokens);
        Assert.Equal(42, consumption.OutputTokens);
        Assert.Equal("https://example.org/cfp", consumption.CallForPapersUrl);
        Assert.InRange(consumption.Timestamp, extractionStartedAt, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ExtractAsync_RecordsConsumptionBeforeParsingModelResponse()
    {
        var repository = new StubTokensConsumptionRepository();
        var service = CreateService("not-json", repository);

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => service.ExtractAsync("https://example.org/cfp", "user-123"));

        Assert.Equal(EventDataExtractionErrorCode.InvalidModelResponse, exception.ErrorCode);
        Assert.Single(repository.Values);
    }

    [Fact]
    public async Task ExtractAsync_ContinuesWhenConsumptionCannotBeRecorded()
    {
        var storageException = new InvalidOperationException("Storage unavailable.");
        var repository = new StubTokensConsumptionRepository(storageException);
        var logger = new RecordingLogger<EventDataExtractionService>();
        var service = CreateService(
            """{"name":"Example Conf","location":null,"officialWebsiteUrl":null,"callForPapersUrl":null,"callForPapersDeadline":null,"startDate":null,"endDate":null}""",
            repository,
            logger);

        var result = await service.ExtractAsync("https://example.org/cfp", "user-123");

        Assert.Equal("Example Conf", result.Name);
        Assert.Equal(1, repository.Attempts);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Same(storageException, error.Exception);
    }

    [Fact]
    public async Task ExtractAsync_DoesNotRecordConsumptionWhenModelCallFails()
    {
        var repository = new StubTokensConsumptionRepository();
        var modelException = new InvalidOperationException("Model unavailable.");
        var service = new EventDataExtractionService(
            new StubPageFetcher("<html></html>"),
            new AngleSharpEventHtmlCleaner(),
            new StubChatClient("{}", modelException),
            repository,
            NullLogger<EventDataExtractionService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExtractAsync("https://example.org/cfp", "user-123"));

        Assert.Same(modelException, exception);
        Assert.Equal(0, repository.Attempts);
    }

    [Fact]
    public async Task ExtractAsync_UsesNormalizedSourceUrlAsCallForPapersUrl()
    {
        var service = CreateService(
            """{"name":"Example Conf","location":null,"officialWebsiteUrl":null,"callForPapersUrl":"https://model.example/cfp","callForPapersDeadline":null,"startDate":null,"endDate":null}""");

        var result = await service.ExtractAsync("HTTPS://EXAMPLE.ORG:443/cfp", "user-123");

        Assert.Equal("https://example.org/cfp", result.CallForPapersUrl);
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
        CreateService(content, new StubTokensConsumptionRepository());

    private static EventDataExtractionService CreateService(
        string content,
        ITokensConsumptionRepository repository,
        ILogger<EventDataExtractionService>? logger = null) =>
        new(
            new StubPageFetcher("<html></html>"),
            new AngleSharpEventHtmlCleaner(),
            new StubChatClient(content),
            repository,
            logger ?? NullLogger<EventDataExtractionService>.Instance);

    private sealed class StubPageFetcher(string html) : IEventPageFetcher
    {
        public Task<string> FetchAsync(string sourcePageUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(html);
    }

    private sealed class StubChatClient(string content, Exception? completionException = null) : IEventDataChatClient
    {
        public string DeploymentName => "test-deployment";

        public string Prompt { get; private set; } = string.Empty;

        public Task<EventDataModelResponse> CompleteAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            this.Prompt = prompt;
            if (completionException is not null)
            {
                return Task.FromException<EventDataModelResponse>(completionException);
            }

            return Task.FromResult(new EventDataModelResponse(content, this.DeploymentName, 125, 42));
        }
    }

    private sealed class StubTokensConsumptionRepository(Exception? createException = null)
        : ITokensConsumptionRepository
    {
        public List<TokensConsumption> Values { get; } = [];

        public int Attempts { get; private set; }

        public Task<TokensConsumption> CreateAsync(
            TokensConsumption value,
            CancellationToken cancellationToken = default)
        {
            this.Attempts++;
            if (createException is not null)
            {
                return Task.FromException<TokensConsumption>(createException);
            }

            this.Values.Add(value);
            return Task.FromResult(value);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, exception));
    }
}