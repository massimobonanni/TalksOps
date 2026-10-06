using System.Net;
using System.Net.Http.Json;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;

namespace TalksOps.ApiClient;

/// <summary>HTTP implementation of <see cref="ITalksOpsApiClient"/>.</summary>
public sealed class TalksOpsApiClient(HttpClient httpClient, ICurrentUserContext currentUser)
    : ITalksOpsApiClient
{
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ICurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

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
    public Task CancelProposalAsync(
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