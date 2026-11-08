using Azure;
using Azure.Data.Tables;
using System.Text.Json;
using TalksOps.Core.Domain;
using TalksOps.Storage;

namespace TalksOps.Storage.Tests;

/// <summary>Verifies feedback validation, Azure Table mapping, partition queries, and ordering.</summary>
public sealed class FeedbackServiceTests
{
    [Fact]
    public async Task SaveSettingsAsync_StoresUtcValuesWithRequiredKeys()
    {
        var eventTable = new RecordingTableClient();
        var service = new FeedbackService(eventTable, new RecordingTableClient());
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var validFrom = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.FromHours(2));
        var validTo = validFrom.AddHours(1);

        var saved = await service.SaveSettingsAsync(new FeedbackSettings
        {
            SessionId = sessionId,
            EventId = eventId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            TimeZoneId = "Europe/Rome"
        });

        var entity = Assert.Single(eventTable.UpsertedEntities);
        Assert.Equal($"feedbacksettings|{sessionId:N}", entity.PartitionKey);
        Assert.Equal(sessionId.ToString("N"), entity.RowKey);
        Assert.Equal(sessionId.ToString("N"), entity.GetString(nameof(FeedbackSettings.SessionId)));
        Assert.Equal(eventId.ToString("N"), entity.GetString(nameof(FeedbackSettings.EventId)));
        Assert.Equal(validFrom.ToUniversalTime(), entity.GetDateTimeOffset(nameof(FeedbackSettings.ValidFrom)));
        Assert.Equal(validTo.ToUniversalTime(), entity.GetDateTimeOffset(nameof(FeedbackSettings.ValidTo)));
        Assert.Equal("Europe/Rome", entity.GetString(nameof(FeedbackSettings.TimeZoneId)));
        Assert.Equal(TimeSpan.Zero, saved.ValidFrom.Offset);
        Assert.Equal(TimeSpan.Zero, saved.ValidTo.Offset);
    }

    [Theory]
    [InlineData(true, true, true, true, "Session title")]
    [InlineData(false, true, true, true, null)]
    [InlineData(true, false, true, true, null)]
    [InlineData(true, true, false, true, null)]
    [InlineData(true, true, true, false, null)]
    public async Task GetSessionTitleAsync_UsesPointReadsWithoutUserContext(
        bool hasSettings, bool hasReference, bool hasSession, bool hasEvent, string? expected)
    {
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var settings = new TableEntity($"feedbacksettings|{sessionId:N}", $"{sessionId:N}")
        {
            ["ValidFrom"] = DateTimeOffset.UtcNow,
            ["ValidTo"] = DateTimeOffset.UtcNow.AddHours(1),
            ["TimeZoneId"] = "UTC"
        };
        if (hasReference)
        {
            settings["EventId"] = $"{eventId:N}";
        }

        var session = new TableEntity($"sessions|{eventId:N}", $"{sessionId:N}")
        {
            ["OwnerId"] = "owner", ["Title"] = "Session title"
        };
        var parent = new TableEntity("events|owner", $"{eventId:N}");
        var entities = new List<TableEntity>();
        if (hasSettings) entities.Add(settings);
        if (hasSession) entities.Add(session);
        if (hasEvent) entities.Add(parent);
        var table = new RecordingTableClient(entities.ToArray());
        var service = new FeedbackService(table, new RecordingTableClient());

        Assert.Equal(expected, await service.GetSessionTitleAsync(sessionId));
        Assert.Null(table.QueryFilter);
        Assert.Equal(($"feedbacksettings|{sessionId:N}", $"{sessionId:N}"), table.PointReads[0]);
        if (hasSettings && hasReference)
        {
            Assert.Equal(($"sessions|{eventId:N}", $"{sessionId:N}"), table.PointReads[1]);
            if (hasSession)
            {
                Assert.Equal(("events|owner", $"{eventId:N}"), table.PointReads[2]);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_StoresFeedbackAndServerGeneratedMetadata()
    {
        var feedbackTable = new RecordingTableClient();
        var service = new FeedbackService(new RecordingTableClient(), feedbackTable);
        var sessionId = Guid.NewGuid();

        var created = await service.CreateAsync(
            sessionId,
            new SessionFeedbackSubmission
            {
                SessionRating = 8,
                SpeakerRating = 9,
                Comment = "Clear and engaging."
            },
            new SessionFeedbackMetadata
            {
                IpAddress = "192.0.2.1",
                UserAgent = "Test browser"
            });

        var entity = Assert.Single(feedbackTable.AddedEntities);
        Assert.Equal($"feedback|{sessionId:N}", entity.PartitionKey);
        Assert.Equal(created.Id.ToString("N"), entity.RowKey);
        Assert.Equal(sessionId.ToString("N"), entity.GetString(nameof(SessionFeedback.SessionId)));
        Assert.Equal(8, entity.GetInt32(nameof(SessionFeedback.SessionRating)));
        Assert.Equal(9, entity.GetInt32(nameof(SessionFeedback.SpeakerRating)));
        Assert.Equal("Clear and engaging.", entity.GetString(nameof(SessionFeedback.Comment)));
        Assert.Equal(created.Timestamp, entity.GetDateTimeOffset("FeedbackTimestamp"));
        Assert.Equal(TimeSpan.Zero, created.Timestamp.Offset);

        using var metadata = JsonDocument.Parse(entity.GetString("MetadataJson")!);
        Assert.Equal("192.0.2.1", metadata.RootElement.GetProperty("IpAddress").GetString());
        Assert.Equal("Test browser", metadata.RootElement.GetProperty("UserAgent").GetString());
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidRatingsWithoutWriting()
    {
        var feedbackTable = new RecordingTableClient();
        var service = new FeedbackService(new RecordingTableClient(), feedbackTable);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new SessionFeedbackSubmission { SessionRating = 0, SpeakerRating = 11 },
            new SessionFeedbackMetadata()));

        Assert.Empty(feedbackTable.AddedEntities);
    }

    [Fact]
    public async Task ImportSessionAsync_RemapsIdentityAndStoresNoRequestMetadata()
    {
        var eventTable = new RecordingTableClient();
        var feedbackTable = new RecordingTableClient();
        var service = new FeedbackService(eventTable, feedbackTable);
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-10-23T09:00:00+02:00");

        await service.ImportSessionAsync(
            sessionId,
            new FeedbackSettings
            {
                SessionId = sessionId,
                EventId = eventId,
                ValidFrom = timestamp,
                ValidTo = timestamp.AddHours(1),
                TimeZoneId = "Europe/Rome"
            },
            [new SessionFeedbackImport
            {
                SessionRating = 8,
                SpeakerRating = 9,
                Comment = "Useful",
                Timestamp = timestamp
            }]);

        var settings = Assert.Single(eventTable.UpsertedEntities);
        Assert.Equal($"feedbacksettings|{sessionId:N}", settings.PartitionKey);
        Assert.Equal(eventId.ToString("N"), settings.GetString(nameof(FeedbackSettings.EventId)));
        var feedback = Assert.Single(feedbackTable.AddedEntities);
        Assert.Equal($"feedback|{sessionId:N}", feedback.PartitionKey);
        Assert.NotEqual(Guid.Empty.ToString("N"), feedback.RowKey);
        Assert.Equal(sessionId.ToString("N"), feedback.GetString(nameof(SessionFeedback.SessionId)));
        Assert.Equal(timestamp.ToUniversalTime(), feedback.GetDateTimeOffset("FeedbackTimestamp"));
        using var metadata = JsonDocument.Parse(feedback.GetString("MetadataJson")!);
        Assert.Null(metadata.RootElement.GetProperty("IpAddress").GetString());
        Assert.Null(metadata.RootElement.GetProperty("UserAgent").GetString());
    }

    [Fact]
    public async Task ImportSessionAsync_ValidatesAllEntriesBeforeWriting()
    {
        var eventTable = new RecordingTableClient();
        var feedbackTable = new RecordingTableClient();
        var service = new FeedbackService(eventTable, feedbackTable);
        var sessionId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportSessionAsync(
            sessionId,
            new FeedbackSettings
            {
                SessionId = sessionId,
                EventId = Guid.NewGuid(),
                ValidFrom = DateTimeOffset.Parse("2026-10-23T10:00:00Z"),
                ValidTo = DateTimeOffset.Parse("2026-10-23T09:00:00Z"),
                TimeZoneId = "Europe/Rome"
            },
            [new SessionFeedbackImport
            {
                SessionRating = 8,
                SpeakerRating = 9,
                Timestamp = DateTimeOffset.UtcNow
            }]));

        Assert.Empty(eventTable.UpsertedEntities);
        Assert.Empty(feedbackTable.AddedEntities);
    }

    [Fact]
    public async Task SaveSettingsAsync_RejectsInvalidIntervalWithoutWriting()
    {
        var eventTable = new RecordingTableClient();
        var service = new FeedbackService(eventTable, new RecordingTableClient());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingsAsync(new FeedbackSettings
        {
            SessionId = Guid.NewGuid(),
            ValidFrom = DateTimeOffset.Parse("2026-10-23T10:00:00Z"),
            ValidTo = DateTimeOffset.Parse("2026-10-23T09:00:00Z"),
            TimeZoneId = "Europe/Rome"
        }));

        Assert.Empty(eventTable.UpsertedEntities);
    }

    [Fact]
    public async Task ListBySessionAsync_QueriesPartitionAndSortsNewestFirst()
    {
        var sessionId = Guid.NewGuid();
        var older = CreateFeedbackEntity(sessionId, Guid.NewGuid(), DateTimeOffset.Parse("2026-10-23T09:00:00Z"));
        var newer = CreateFeedbackEntity(sessionId, Guid.NewGuid(), DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        var equalTimeLaterId = CreateFeedbackEntity(
            sessionId,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        var feedbackTable = new RecordingTableClient(older, newer, equalTimeLaterId);
        var service = new FeedbackService(new RecordingTableClient(), feedbackTable);

        var feedback = await service.ListBySessionAsync(sessionId);

        Assert.Equal($"PartitionKey eq 'feedback|{sessionId:N}'", feedbackTable.QueryFilter);
        Assert.Equal(
            [newer.RowKey, equalTimeLaterId.RowKey, older.RowKey],
            feedback.Select(value => value.Id.ToString("N")));
        Assert.All(feedback, value => Assert.Equal("192.0.2.1", value.Metadata.IpAddress));
    }

    private static TableEntity CreateFeedbackEntity(Guid sessionId, Guid id, DateTimeOffset timestamp) => new(
        $"feedback|{sessionId:N}",
        id.ToString("N"))
    {
        [nameof(SessionFeedback.SessionId)] = sessionId.ToString("N"),
        [nameof(SessionFeedback.SessionRating)] = 7,
        [nameof(SessionFeedback.SpeakerRating)] = 8,
        [nameof(SessionFeedback.Timestamp)] = timestamp,
        ["MetadataJson"] = """{"IpAddress":"192.0.2.1","UserAgent":"Test browser"}"""
    };

    private sealed class RecordingTableClient(params TableEntity[] queryEntities) : TableClient
    {
        public List<TableEntity> AddedEntities { get; } = [];

        public List<TableEntity> UpsertedEntities { get; } = [];

        public string? QueryFilter { get; private set; }

        public List<(string PartitionKey, string RowKey)> PointReads { get; } = [];

        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
            string partitionKey, string rowKey, IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            this.PointReads.Add((partitionKey, rowKey));
            var entity = queryEntities.SingleOrDefault(value => value.PartitionKey == partitionKey && value.RowKey == rowKey);
            return Task.FromResult<NullableResponse<T>>(new EntityResponse<T>(entity is null ? default : (T)(object)entity));
        }

        public override Task<Response> AddEntityAsync<T>(
            T entity,
            CancellationToken cancellationToken = default)
        {
            this.AddedEntities.Add((TableEntity)(object)entity);
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response> UpsertEntityAsync<T>(
            T entity,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            this.UpsertedEntities.Add((TableEntity)(object)entity);
            return Task.FromResult<Response>(null!);
        }

        public override AsyncPageable<T> QueryAsync<T>(
            string filter,
            int? maxPerPage = null,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            this.QueryFilter = filter;
            var entities = queryEntities.Cast<T>().ToArray();
            return AsyncPageable<T>.FromPages([Page<T>.FromValues(entities, null, null!)]);
        }
    }

    private sealed class EntityResponse<T>(T? entity) : NullableResponse<T>
    {
        public override bool HasValue => entity is not null;
        public override T? Value => entity;
        public override Response GetRawResponse() => null!;
    }
}
