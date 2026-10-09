using System.Text.Json;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Export;

namespace TalksOps.Core.Services;

/// <summary>Implements JSON export and additive import of events and sessions for the current user.</summary>
public sealed class EventImportExportService(
    IEventRepository events,
    ISessionProposalRepository proposals,
    ICurrentUserContext currentUser) : IEventImportExportService
{
    // Repository searches clamp the page size to 100, so export pages through results at that size.
    private const int ExportPageSize = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IEventRepository events = events ?? throw new ArgumentNullException(nameof(events));
    private readonly ISessionProposalRepository proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    private readonly ICurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public async Task<EventsExportDocument> ExportAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new ArgumentException("The export start date must be on or before the end date.", nameof(from));
        }

        var ownerId = this.currentUser.UserId;
        var exported = new List<ExportedEvent>();
        var page = 1;
        while (true)
        {
            var result = await this.events.SearchAsync(
                ownerId,
                new EventSearchCriteria { Page = page, PageSize = ExportPageSize },
                cancellationToken);

            foreach (var value in result.Items.Where(value => Overlaps(value, from, to)))
            {
                var sessions = await this.proposals.ListByEventAsync(ownerId, value.Id, cancellationToken);
                exported.Add(ToExported(value, sessions));
            }

            if (result.Items.Count == 0 || page * result.PageSize >= result.TotalCount)
            {
                break;
            }

            page++;
        }

        return new EventsExportDocument
        {
            From = from,
            To = to,
            Events = exported
        };
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        Stream destination,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var document = await this.ExportAsync(from, to, cancellationToken);
        await JsonSerializer.SerializeAsync(destination, document, SerializerOptions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EventImportResult> ImportAsync(
        EventsExportDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != EventsExportDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported export schema version {document.SchemaVersion}.");
        }

        // Map and validate everything up front so an invalid file does not leave a partial import behind.
        var ownerId = this.currentUser.UserId;
        var errors = new List<string>();
        var toCreate = new List<(Event Event, IReadOnlyList<SessionProposal> Sessions)>();
        foreach (var (exportedEvent, eventIndex) in (document.Events ?? []).Select((value, index) => (value, index)))
        {
            if (exportedEvent is null)
            {
                errors.Add($"Event #{eventIndex + 1} is empty.");
                continue;
            }

            // New identifiers guarantee the import only adds data and never collides with existing rows.
            var value = new Event
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Name = exportedEvent.Name,
                Location = exportedEvent.Location,
                OfficialWebsiteUrl = exportedEvent.OfficialWebsiteUrl,
                CallForPapersUrl = exportedEvent.CallForPapersUrl,
                StartDate = exportedEvent.StartDate,
                EndDate = exportedEvent.EndDate,
                Costs = exportedEvent.Costs ?? new Dictionary<string, decimal>()
            };
            errors.AddRange(EventRules.Validate(value).Select(error => $"Event #{eventIndex + 1}: {error}"));

            var sessions = new List<SessionProposal>();
            foreach (var (exportedSession, sessionIndex) in (exportedEvent.Sessions ?? []).Select((s, i) => (s, i)))
            {
                var prefix = $"Event #{eventIndex + 1}, session #{sessionIndex + 1}:";
                if (exportedSession is null)
                {
                    errors.Add($"{prefix} the session is empty.");
                    continue;
                }

                if (!Enum.IsDefined(exportedSession.Status))
                {
                    errors.Add($"{prefix} the session status is invalid.");
                }

                var session = new SessionProposal
                {
                    Id = Guid.NewGuid(),
                    EventId = value.Id,
                    OwnerId = ownerId,
                    Title = exportedSession.Title,
                    Abstract = exportedSession.Abstract,
                    Notes = exportedSession.Notes ?? string.Empty,
                    Status = exportedSession.Status,
                    CreatedAt = exportedSession.CreatedAt,
                    UpdatedAt = exportedSession.UpdatedAt
                };
                errors.AddRange(SessionProposalRules.Validate(session).Select(error => $"{prefix} {error}"));
                sessions.Add(session);
            }

            toCreate.Add((value, sessions));
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", errors));
        }

        var sessionCount = 0;
        foreach (var (value, sessions) in toCreate)
        {
            await this.events.CreateAsync(value, cancellationToken);
            foreach (var session in sessions)
            {
                await this.proposals.CreateAsync(session, cancellationToken);
                sessionCount++;
            }
        }

        return new EventImportResult(toCreate.Count, sessionCount);
    }

    /// <inheritdoc />
    public async Task<EventImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        EventsExportDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<EventsExportDocument>(source, SerializerOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The import file is not a valid TalksOps events export.", exception);
        }

        return document is null
            ? throw new InvalidDataException("The import file is empty.")
            : await this.ImportAsync(document, cancellationToken);
    }

    // An event is exported when its [StartDate, EndDate] interval intersects the requested range.
    private static bool Overlaps(Event value, DateOnly? from, DateOnly? to) =>
        (from is null || value.EndDate >= from) && (to is null || value.StartDate <= to);

    private static ExportedEvent ToExported(Event value, IReadOnlyList<SessionProposal> sessions) => new()
    {
        Id = value.Id,
        Name = value.Name,
        Location = value.Location,
        OfficialWebsiteUrl = value.OfficialWebsiteUrl,
        CallForPapersUrl = value.CallForPapersUrl,
        StartDate = value.StartDate,
        EndDate = value.EndDate,
        Costs = value.Costs,
        Sessions = sessions
            .OrderBy(session => session.CreatedAt)
            .Select(session => new ExportedSession
            {
                Id = session.Id,
                Title = session.Title,
                Abstract = session.Abstract,
                Notes = session.Notes,
                Status = session.Status,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt
            })
            .ToArray()
    };
}
