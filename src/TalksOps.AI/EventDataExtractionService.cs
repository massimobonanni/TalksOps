using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TalksOps.AI;

/// <summary>Fetches a CFP page and extracts event details using a Foundry deployment.</summary>
public sealed class EventDataExtractionService(
    IEventPageFetcher pageFetcher,
    IEventDataChatClient chatClient,
    ILogger<EventDataExtractionService> logger) : IEventDataExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<ExtractedEventData> ExtractAsync(
        string sourcePageUrl,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePageUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(pageFetcher);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation(
            "Starting event extraction for user {UserId} with deployment {DeploymentName}.",
            userId,
            chatClient.DeploymentName);

        var html = await pageFetcher.FetchAsync(sourcePageUrl, cancellationToken);
        var prompt = PromptProvider.LoadExtractEventDataPromptText()
            .Replace("[SOURCE_PAGE_URL]", sourcePageUrl, StringComparison.Ordinal)
            .Replace("[HTML_CONTENT]", html, StringComparison.Ordinal);
        var response = await chatClient.CompleteAsync(prompt, cancellationToken);
        logger.LogInformation(
            "Event extraction model usage for user {UserId} with deployment {DeploymentName}: input tokens {InputTokens}, output tokens {OutputTokens}.",
            userId,
            response.DeploymentName,
            response.InputTokens,
            response.OutputTokens);

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.InvalidModelResponse,
                "The model returned an empty response.");
        }

        ExtractedEventData? result;
        try
        {
            result = JsonSerializer.Deserialize<ExtractedEventData>(
                response.Content,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.InvalidModelResponse,
                "The model returned invalid event data.",
                exception);
        }

        if (result is null)
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.InvalidModelResponse,
                "The model returned invalid event data.");
        }

        var normalized = Normalize(result);
        if (normalized is
            {
                Name: null,
                Location: null,
                OfficialWebsiteUrl: null,
                CallForPapersUrl: null,
                CallForPapersDeadline: null,
                StartDate: null,
                EndDate: null
            })
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.NoEventData,
                "No event information could be reliably extracted from the page.");
        }

        return normalized;
    }

    private static ExtractedEventData Normalize(ExtractedEventData result) =>
        new(
            NormalizeText(result.Name),
            NormalizeText(result.Location),
            NormalizeUrl(result.OfficialWebsiteUrl),
            NormalizeUrl(result.CallForPapersUrl),
            NormalizeDate(result.CallForPapersDeadline),
            NormalizeDate(result.StartDate),
            NormalizeDate(result.EndDate));

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0)
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static string? NormalizeDate(string? value) =>
        DateOnly.TryParseExact(
            value?.Trim(),
            "dd/MM/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : null;
}