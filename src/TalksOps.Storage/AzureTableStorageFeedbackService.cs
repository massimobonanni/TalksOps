using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;

namespace TalksOps.Storage;

/// <summary>Persists feedback settings and submissions in Azure Table Storage.</summary>
public sealed class AzureTableStorageFeedbackService(
    TableClient eventTable,
    [FromKeyedServices(AzureTablesStorageExtensions.FeedbackTableClientKey)] TableClient feedbackTable)
    : IFeedbackService
{
    private readonly TableClient eventTable = eventTable ?? throw new ArgumentNullException(nameof(eventTable));
    private readonly TableClient feedbackTable = feedbackTable ?? throw new ArgumentNullException(nameof(feedbackTable));

    /// <inheritdoc />
    public async Task<string?> GetSessionTitleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var settings = await this.GetSettingsAsync(sessionId, cancellationToken);
        if (settings is null || settings.EventId == Guid.Empty)
        {
            return null;
        }

        var response = await this.eventTable.GetEntityIfExistsAsync<TableEntity>(
            AzureTableEventRepository.ProposalPartitionKey(settings.EventId),
            AzureTableEventRepository.Key(sessionId),
            cancellationToken: cancellationToken);
        if (!response.HasValue)
        {
            return null;
        }

        var ownerId = response.Value!.GetString(nameof(SessionProposal.OwnerId))
            ?? throw new InvalidOperationException("The stored session is missing its owner.");
        var parent = await this.eventTable.GetEntityIfExistsAsync<TableEntity>(
            AzureTableEventRepository.EventPartitionKey(ownerId),
            AzureTableEventRepository.Key(settings.EventId),
            cancellationToken: cancellationToken);
        if (!parent.HasValue)
        {
            return null;
        }

        return response.Value.GetString(nameof(SessionProposal.Title))
            ?? throw new InvalidOperationException("The stored session is missing its title.");
    }

    /// <inheritdoc />
    public async Task<FeedbackSettings?> GetSettingsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        var response = await this.eventTable.GetEntityIfExistsAsync<TableEntity>(
            SettingsPartitionKey(sessionId),
            AzureTableEventRepository.Key(sessionId),
            cancellationToken: cancellationToken);

        return response.HasValue ? FromSettingsEntity(response.Value!) : null;
    }

    /// <inheritdoc />
    public async Task<FeedbackSettings> SaveSettingsAsync(
        FeedbackSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        var normalized = settings with
        {
            ValidFrom = settings.ValidFrom.ToUniversalTime(),
            ValidTo = settings.ValidTo.ToUniversalTime()
        };
        await this.eventTable.UpsertEntityAsync(
            ToSettingsEntity(normalized),
            TableUpdateMode.Replace,
            cancellationToken);
        return normalized;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionFeedback>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);

        var feedback = new List<SessionFeedback>();
        await foreach (var entity in this.feedbackTable.QueryAsync<TableEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {FeedbackPartitionKey(sessionId)}"),
            cancellationToken: cancellationToken))
        {
            feedback.Add(FromFeedbackEntity(entity));
        }

        return feedback
            .OrderByDescending(value => value.Timestamp)
            .ThenBy(value => value.Id)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<SessionFeedback> CreateAsync(
        Guid sessionId,
        SessionFeedbackSubmission submission,
        SessionFeedbackMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateSubmission(submission);

        var feedback = new SessionFeedback
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            SessionRating = submission.SessionRating,
            SpeakerRating = submission.SpeakerRating,
            Comment = submission.Comment,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = metadata
        };

        await this.feedbackTable.AddEntityAsync(ToFeedbackEntity(feedback), cancellationToken);
        return feedback;
    }

    /// <inheritdoc />
    public async Task ImportSessionAsync(
        Guid sessionId,
        FeedbackSettings? settings,
        IReadOnlyList<SessionFeedbackImport> feedback,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(feedback);

        if (settings is not null)
        {
            if (settings.SessionId != sessionId || settings.EventId == Guid.Empty)
            {
                throw new ArgumentException("Imported settings must reference the new session and event.", nameof(settings));
            }

            ValidateSettings(settings);
        }

        foreach (var item in feedback)
        {
            if (item is null)
            {
                throw new ArgumentException("Imported feedback cannot contain empty entries.", nameof(feedback));
            }

            var errors = FeedbackRules.ValidateImportedFeedback(item);
            if (errors.Count > 0)
            {
                throw new ArgumentException(string.Join(" ", errors), nameof(feedback));
            }
        }

        if (settings is not null)
        {
            var normalized = settings with
            {
                ValidFrom = settings.ValidFrom.ToUniversalTime(),
                ValidTo = settings.ValidTo.ToUniversalTime()
            };
            await this.eventTable.UpsertEntityAsync(ToSettingsEntity(normalized), TableUpdateMode.Replace, cancellationToken);
        }

        foreach (var item in feedback)
        {
            var imported = new SessionFeedback
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                SessionRating = item.SessionRating,
                SpeakerRating = item.SpeakerRating,
                Comment = item.Comment,
                Timestamp = item.Timestamp.ToUniversalTime(),
                Metadata = new SessionFeedbackMetadata()
            };
            await this.feedbackTable.AddEntityAsync(ToFeedbackEntity(imported), cancellationToken);
        }
    }

    private static void ValidateSessionId(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A session is required.", nameof(sessionId));
        }
    }

    private static void ValidateSettings(FeedbackSettings settings)
    {
        var errors = FeedbackRules.Validate(settings);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        }
    }

    private static void ValidateSubmission(SessionFeedbackSubmission submission)
    {
        var errors = FeedbackRules.ValidateSubmission(submission);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(submission));
        }
    }

    private static FeedbackSettings FromSettingsEntity(TableEntity entity) => new()
    {
        SessionId = Guid.ParseExact(entity.RowKey, "N"),
        EventId = entity.GetString(nameof(FeedbackSettings.EventId)) is { } eventId
            ? Guid.ParseExact(eventId, "N")
            : Guid.Empty,
        ValidFrom = entity.GetDateTimeOffset(nameof(FeedbackSettings.ValidFrom))!.Value,
        ValidTo = entity.GetDateTimeOffset(nameof(FeedbackSettings.ValidTo))!.Value,
        TimeZoneId = entity.GetString(nameof(FeedbackSettings.TimeZoneId))!
    };

    private static TableEntity ToSettingsEntity(FeedbackSettings value) => new(
        SettingsPartitionKey(value.SessionId),
        AzureTableEventRepository.Key(value.SessionId))
    {
        [nameof(FeedbackSettings.SessionId)] = value.SessionId.ToString("N"),
        [nameof(FeedbackSettings.EventId)] = value.EventId.ToString("N"),
        [nameof(FeedbackSettings.ValidFrom)] = value.ValidFrom,
        [nameof(FeedbackSettings.ValidTo)] = value.ValidTo,
        [nameof(FeedbackSettings.TimeZoneId)] = value.TimeZoneId
    };

    private static SessionFeedback FromFeedbackEntity(TableEntity entity)
    {
        var metadataJson = entity.GetString("MetadataJson")
            ?? throw new InvalidOperationException("The stored feedback is missing request metadata.");
        var metadata = JsonSerializer.Deserialize<SessionFeedbackMetadata>(metadataJson)
            ?? throw new InvalidOperationException("The stored feedback request metadata is invalid.");

        return new SessionFeedback
        {
            Id = Guid.ParseExact(entity.RowKey, "N"),
            SessionId = Guid.ParseExact(entity.GetString(nameof(SessionFeedback.SessionId))!, "N"),
            SessionRating = entity.GetInt32(nameof(SessionFeedback.SessionRating))!.Value,
            SpeakerRating = entity.GetInt32(nameof(SessionFeedback.SpeakerRating))!.Value,
            Comment = entity.GetString(nameof(SessionFeedback.Comment)),
            Timestamp = entity.GetDateTimeOffset("FeedbackTimestamp")
                ?? entity.GetDateTimeOffset(nameof(SessionFeedback.Timestamp))
                ?? throw new InvalidOperationException("The stored feedback is missing its submission timestamp."),
            Metadata = metadata
        };
    }

    private static TableEntity ToFeedbackEntity(SessionFeedback value)
    {
        var entity = new TableEntity(
            FeedbackPartitionKey(value.SessionId),
            AzureTableEventRepository.Key(value.Id))
        {
            [nameof(SessionFeedback.SessionId)] = value.SessionId.ToString("N"),
            [nameof(SessionFeedback.SessionRating)] = value.SessionRating,
            [nameof(SessionFeedback.SpeakerRating)] = value.SpeakerRating,
            ["FeedbackTimestamp"] = value.Timestamp.ToUniversalTime(),
            ["MetadataJson"] = JsonSerializer.Serialize(value.Metadata)
        };

        if (value.Comment is not null)
        {
            entity[nameof(SessionFeedback.Comment)] = value.Comment;
        }

        return entity;
    }

    private static string SettingsPartitionKey(Guid sessionId) =>
        $"feedbacksettings|{AzureTableEventRepository.Key(sessionId)}";

    private static string FeedbackPartitionKey(Guid sessionId) =>
        $"feedback|{AzureTableEventRepository.Key(sessionId)}";
}
