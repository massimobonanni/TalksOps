using System.Text.Json;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Export;

namespace TalksOps.Core.Services;

/// <summary>Implements JSON export and additive import of events and sessions for the current user.</summary>
public sealed class EventImportExportService(
    IEventRepository events,
    ISessionProposalRepository proposals,
    ICurrentUserContext currentUser,
    IFeedbackService feedback) : IEventImportExportService
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
    private readonly IFeedbackService feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));

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
                exported.Add(await this.ToExportedAsync(value, sessions, cancellationToken));
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
        var toCreate = new List<ImportedEvent>();
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
                CallForPapersDeadline = exportedEvent.CallForPapersDeadline,
                StartDate = exportedEvent.StartDate,
                EndDate = exportedEvent.EndDate,
                Costs = exportedEvent.Costs ?? new Dictionary<string, decimal>()
            };
            errors.AddRange(EventRules.Validate(value).Select(error => $"Event #{eventIndex + 1}: {error}"));

            var sessions = new List<ImportedSession>();
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
                    Abstract = exportedSession.Abstract ?? string.Empty,
                    Notes = exportedSession.Notes ?? string.Empty,
                    SubmissionDate = exportedSession.SubmissionDate,
                    Status = exportedSession.Status,
                    CreatedAt = exportedSession.CreatedAt,
                    UpdatedAt = exportedSession.UpdatedAt
                };
                errors.AddRange(SessionProposalRules.Validate(session).Select(error => $"{prefix} {error}"));

                FeedbackSettings? settings = null;
                if (exportedSession.FeedbackSettings is { } exportedSettings)
                {
                    settings = new FeedbackSettings
                    {
                        SessionId = session.Id,
                        EventId = value.Id,
                        ValidFrom = exportedSettings.ValidFrom,
                        ValidTo = exportedSettings.ValidTo,
                        TimeZoneId = exportedSettings.TimeZoneId
                    };
                    errors.AddRange(FeedbackRules.Validate(settings).Select(error => $"{prefix} feedback settings: {error}"));
                }

                var feedback = new List<SessionFeedbackImport>();
                foreach (var (exportedFeedback, feedbackIndex) in (exportedSession.Feedback ?? []).Select((item, index) => (item, index)))
                {
                    if (exportedFeedback is null)
                    {
                        errors.Add($"{prefix} feedback #{feedbackIndex + 1} is empty.");
                        continue;
                    }

                    var importedFeedback = new SessionFeedbackImport
                    {
                        SessionRating = exportedFeedback.SessionRating,
                        SpeakerRating = exportedFeedback.SpeakerRating,
                        Comment = exportedFeedback.Comment,
                        Timestamp = exportedFeedback.Timestamp
                    };
                    errors.AddRange(FeedbackRules.ValidateImportedFeedback(importedFeedback)
                        .Select(error => $"{prefix} feedback #{feedbackIndex + 1}: {error}"));
                    feedback.Add(importedFeedback);
                }

                sessions.Add(new ImportedSession(session, settings, feedback));
            }

            toCreate.Add(new ImportedEvent(value, sessions));
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", errors));
        }

        var sessionCount = 0;
        var feedbackCount = 0;
        foreach (var item in toCreate)
        {
            await this.events.CreateAsync(item.Event, cancellationToken);
            foreach (var session in item.Sessions)
            {
                await this.proposals.CreateAsync(session.Proposal, cancellationToken);
                if (session.Settings is not null || session.Feedback.Count > 0)
                {
                    await this.feedback.ImportSessionAsync(
                        session.Proposal.Id,
                        session.Settings,
                        session.Feedback,
                        cancellationToken);
                }

                sessionCount++;
                feedbackCount += session.Feedback.Count;
            }
        }

        return new EventImportResult(toCreate.Count, sessionCount, feedbackCount);
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

    private async Task<ExportedEvent> ToExportedAsync(
        Event value,
        IReadOnlyList<SessionProposal> sessions,
        CancellationToken cancellationToken)
    {
        var exportedSessions = new List<ExportedSession>(sessions.Count);
        foreach (var session in sessions.OrderBy(session => session.CreatedAt))
        {
            var settings = await this.feedback.GetSettingsAsync(session.Id, cancellationToken);
            var feedback = await this.feedback.ListBySessionAsync(session.Id, cancellationToken);
            exportedSessions.Add(new ExportedSession
            {
                Title = session.Title,
                Abstract = session.Abstract,
                Notes = session.Notes,
                SubmissionDate = session.SubmissionDate,
                Status = session.Status,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.UpdatedAt,
                FeedbackSettings = settings is null
                    ? null
                    : new ExportedFeedbackSettings
                    {
                        ValidFrom = settings.ValidFrom,
                        ValidTo = settings.ValidTo,
                        TimeZoneId = settings.TimeZoneId
                    },
                Feedback = feedback.Select(item => new ExportedFeedback
                {
                    SessionRating = item.SessionRating,
                    SpeakerRating = item.SpeakerRating,
                    Comment = item.Comment,
                    Timestamp = item.Timestamp
                }).ToArray()
            });
        }

        return new ExportedEvent
        {
            Name = value.Name,
            Location = value.Location,
            OfficialWebsiteUrl = value.OfficialWebsiteUrl,
            CallForPapersUrl = value.CallForPapersUrl,
            CallForPapersDeadline = value.CallForPapersDeadline,
            StartDate = value.StartDate,
            EndDate = value.EndDate,
            Costs = value.Costs,
            Sessions = exportedSessions
        };
    }

    private sealed record ImportedEvent(Event Event, IReadOnlyList<ImportedSession> Sessions);

    private sealed record ImportedSession(
        SessionProposal Proposal,
        FeedbackSettings? Settings,
        IReadOnlyList<SessionFeedbackImport> Feedback);
}
