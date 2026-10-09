using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Export;

namespace TalksOps.ApiClient;

/// <summary>Provides typed access to the TalksOps Functions REST API.</summary>
public interface ITalksOpsApiClient
{
    /// <summary>Searches events for the current user.</summary>
    Task<PagedResponse<EventDto>> SearchEventsAsync(
        EventSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Gets an event by identifier.</summary>
    Task<EventDto?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Creates an event.</summary>
    Task<EventDto> CreateEventAsync(SaveEventRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates an event.</summary>
    Task<EventDto> UpdateEventAsync(
        Guid eventId,
        SaveEventRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an event and its proposals.</summary>
    Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Lists proposals for an event.</summary>
    Task<IReadOnlyList<SessionProposalDto>> ListProposalsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a proposal by identifier.</summary>
    Task<SessionProposalDto?> GetProposalAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a proposal for an event.</summary>
    Task<SessionProposalDto> CreateProposalAsync(
        Guid eventId,
        SaveSessionProposalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a proposal.</summary>
    Task<SessionProposalDto> UpdateProposalAsync(
        Guid eventId,
        Guid proposalId,
        SaveSessionProposalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a proposal's status.</summary>
    Task<SessionProposalDto> ChangeProposalStatusAsync(
        Guid eventId,
        Guid proposalId,
        ChangeProposalStatusRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a proposal.</summary>
    Task DeleteProposalAsync(
        Guid eventId,
        Guid proposalId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the current user's annual calendar events.</summary>
    Task<IReadOnlyList<CalendarEventDto>> GetCalendarAsync(
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>Exports the current user's events and sessions overlapping the optional date range.</summary>
    Task<EventsExportDocument> ExportEventsAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    /// <summary>Adds the events and sessions of an export document to the current user's data.</summary>
    /// <exception cref="HttpRequestException">Thrown with the server validation message when the import is rejected.</exception>
    Task<EventImportResult> ImportEventsAsync(
        EventsExportDocument document,
        CancellationToken cancellationToken = default);
}