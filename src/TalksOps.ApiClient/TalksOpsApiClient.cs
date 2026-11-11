using System.Net;
using System.Net.Http.Json;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Export;

namespace TalksOps.ApiClient;

/// <summary>HTTP implementation of <see cref="ITalksOpsApiClient"/>.</summary>
public sealed class TalksOpsApiClient(HttpClient httpClient, ICurrentUserContext currentUser)
    : ITalksOpsApiClient
{
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ICurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public Task<FeedbackSettingsDto?> GetFeedbackSettingsAsync(
        Guid eventId, Guid proposalId, CancellationToken cancellationToken = default) =>
        this.GetOptionalAsync<FeedbackSettingsDto>(
            $"api/events/{eventId}/proposals/{proposalId}/feedback/settings".WithUserId(this.currentUser.UserId),
            cancellationToken);

    /// <inheritdoc />
    public async Task<FeedbackSettingsDto> SaveFeedbackSettingsAsync(
        Guid eventId, Guid proposalId, SaveFeedbackSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await this.httpClient.PutAsJsonAsync(
            $"api/events/{eventId}/proposals/{proposalId}/feedback/settings",
            new ApiRequest<SaveFeedbackSettingsRequest>(this.currentUser.UserId, request), cancellationToken);
        await EnsureFeedbackSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FeedbackSettingsDto>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionFeedbackDto>> ListFeedbackAsync(
        Guid eventId, Guid proposalId, CancellationToken cancellationToken = default) =>
        this.GetRequiredAsync<IReadOnlyList<SessionFeedbackDto>>(
            $"api/events/{eventId}/proposals/{proposalId}/feedback".WithUserId(this.currentUser.UserId),
            cancellationToken);

    /// <inheritdoc />
    public async Task<ImportFeedbackResultDto> ImportFeedbackAsync(
        Guid eventId, Guid proposalId, IReadOnlyList<ImportFeedbackEntryDto> feedback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        using var response = await this.httpClient.PostAsJsonAsync(
            $"api/events/{eventId}/proposals/{proposalId}/feedback/import",
            new ApiRequest<IReadOnlyList<ImportFeedbackEntryDto>>(this.currentUser.UserId, feedback), cancellationToken);
        await EnsureFeedbackSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportFeedbackResultDto>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    /// <inheritdoc />
    public Task<PublicFeedbackDto?> GetPublicFeedbackAsync(
        Guid sessionId, CancellationToken cancellationToken = default) =>
        this.GetOptionalAsync<PublicFeedbackDto>($"api/feedback/{sessionId}", cancellationToken);

    /// <inheritdoc />
    public async Task SubmitFeedbackAsync(
        Guid sessionId, SubmitFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await this.httpClient.PostAsJsonAsync(
            $"api/feedback/{sessionId}", request, cancellationToken);
        await EnsureFeedbackSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureFeedbackSuccessAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            ApiErrorDto? error = null;
            if (!string.IsNullOrWhiteSpace(message)
                && response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                try
                {
                    error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorDto>(
                        message, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                }
                catch (System.Text.Json.JsonException exception)
                {
                    throw new HttpRequestException(
                        $"Feedback request failed with status {(int)response.StatusCode} and an invalid error body.",
                        exception, response.StatusCode);
                }
            }

            throw new HttpRequestException(
                error?.Message ?? $"Feedback request failed with status {(int)response.StatusCode}.",
                null, response.StatusCode);
        }
    }

    /// <inheritdoc />
    public async Task<PagedResponse<EventDto>> SearchEventsAsync(
        EventSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = new List<string>();
        AddQuery(query, "year", request.Year?.ToString());
        AddQuery(query, "name", request.Name);
        AddQuery(query, "location", request.Location);
        AddQuery(query, "page", request.Page.ToString());
        AddQuery(query, "pageSize", request.PageSize.ToString());
        var path = $"api/events?{string.Join('&', query)}".WithUserId(this.currentUser.UserId);
        return await this.GetRequiredAsync<PagedResponse<EventDto>>(path, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EventDto?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        this.GetOptionalAsync<EventDto>($"api/events/{eventId}".WithUserId(this.currentUser.UserId), cancellationToken);

    /// <inheritdoc />
    public Task<EventDto> CreateEventAsync(SaveEventRequest request, CancellationToken cancellationToken = default) =>
        this.PostAsync<EventDto>("api/events", request, cancellationToken);

    /// <inheritdoc />
    public Task<EventDto> UpdateEventAsync(
        Guid eventId,
        SaveEventRequest request,
        CancellationToken cancellationToken = default) =>
        this.PutAsync<EventDto>($"api/events/{eventId}", request, cancellationToken);

    /// <inheritdoc />
    public Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        this.DeleteAsync($"api/events/{eventId}".WithUserId(this.currentUser.UserId), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionProposalDto>> ListProposalsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default) =>
        await this.GetRequiredAsync<IReadOnlyList<SessionProposalDto>>(
            $"api/events/{eventId}/proposals".WithUserId(this.currentUser.UserId), cancellationToken);

    /// <inheritdoc />
    public Task<SessionProposalDto?> GetProposalAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default) =>
        this.GetOptionalAsync<SessionProposalDto>(
            $"api/events/{eventId}/proposals/{proposalId}".WithUserId(this.currentUser.UserId), cancellationToken);

    /// <inheritdoc />
    public Task<SessionProposalDto> CreateProposalAsync(
        Guid eventId,
        SaveSessionProposalRequest request,
        CancellationToken cancellationToken = default) =>
        this.PostAsync<SessionProposalDto>($"api/events/{eventId}/proposals", request, cancellationToken);

    /// <inheritdoc />
    public Task<SessionProposalDto> UpdateProposalAsync(
        Guid eventId,
        Guid proposalId,
        SaveSessionProposalRequest request,
        CancellationToken cancellationToken = default) =>
        this.PutAsync<SessionProposalDto>($"api/events/{eventId}/proposals/{proposalId}", request, cancellationToken);

    /// <inheritdoc />
    public Task<SessionProposalDto> ChangeProposalStatusAsync(
        Guid eventId,
        Guid proposalId,
        ChangeProposalStatusRequest request,
        CancellationToken cancellationToken = default) =>
        this.PatchAsync<SessionProposalDto>(
            $"api/events/{eventId}/proposals/{proposalId}/status", request, cancellationToken);

    /// <inheritdoc />
    public Task DeleteProposalAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default) =>
        this.DeleteAsync(
            $"api/events/{eventId}/proposals/{proposalId}".WithUserId(this.currentUser.UserId), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEventDto>> GetCalendarAsync(
        int year,
        CancellationToken cancellationToken = default) =>
        await this.GetRequiredAsync<IReadOnlyList<CalendarEventDto>>(
            $"api/calendar?year={year}".WithUserId(this.currentUser.UserId), cancellationToken);

    /// <inheritdoc />
    public Task<EventsExportDocument> ExportEventsAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQuery(query, "from", from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        AddQuery(query, "to", to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var path = (query.Count == 0 ? "api/export" : $"api/export?{string.Join('&', query)}")
            .WithUserId(this.currentUser.UserId);
        return this.GetRequiredAsync<EventsExportDocument>(path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EventImportResult> ImportEventsAsync(
        EventsExportDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var response = await this.httpClient.PostAsJsonAsync(
            "api/import", new ApiRequest<EventsExportDocument>(this.currentUser.UserId, document), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Surface the server's validation message so the user can fix the file.
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(message) ? $"Import failed with status {(int)response.StatusCode}." : message,
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<EventImportResult>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    /// <inheritdoc />
    public async Task<ExtractEventDataResultDto> ExtractEventDataAsync(
        ExtractEventDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await this.httpClient.PostAsJsonAsync(
            "api/events/extract",
            new ApiRequest<ExtractEventDataRequest>(this.currentUser.UserId, request),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken);
            throw new HttpRequestException(
                error?.Message ?? $"Event extraction failed with status {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ExtractEventDataResultDto>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task<TResponse> PostAsync<TResponse>(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PostAsJsonAsync(
            path, new ApiRequest<object>(this.currentUser.UserId, payload), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    private async Task<TResponse> PutAsync<TResponse>(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PutAsJsonAsync(
            path, new ApiRequest<object>(this.currentUser.UserId, payload), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    private async Task<TResponse> PatchAsync<TResponse>(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.PatchAsJsonAsync(
            path, new ApiRequest<object>(this.currentUser.UserId, payload), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new HttpRequestException("The TalksOps API returned an empty response body.");
    }

    private async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.DeleteAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static void AddQuery(ICollection<string> query, string name, string? value)
    {
        if (value is not null)
        {
            query.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }
    }
}