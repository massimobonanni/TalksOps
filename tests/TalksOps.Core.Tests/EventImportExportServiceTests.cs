using System.Text;
using System.Text.Json;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Export;
using TalksOps.Core.Services;

namespace TalksOps.Core.Tests;

public sealed class EventImportExportServiceTests
{
    private const string UserId = "user-1";

    [Fact]
    public async Task ExportAsync_ReturnsOnlyCurrentUserEventsWithSessions()
    {
        var store = new InMemoryStore();
        var own = store.AddEvent(UserId, new DateOnly(2026, 5, 1));
        store.AddEvent("user-2", new DateOnly(2026, 5, 1));
        store.AddSession(own, "Talk A", submissionDate: new DateOnly(2026, 4, 15));

        var document = await CreateService(store).ExportAsync(null, null);

        var exported = Assert.Single(document.Events);
        Assert.Equal(own.Name, exported.Name);
        Assert.Equal(own.StartDate, exported.StartDate);
        var session = Assert.Single(exported.Sessions);
        Assert.Equal("Talk A", session.Title);
        Assert.Equal(new DateOnly(2026, 4, 15), session.SubmissionDate);
    }

    [Fact]
    public async Task ExportAsync_FiltersByOverlappingDateRange()
    {
        var store = new InMemoryStore();
        store.AddEvent(UserId, new DateOnly(2026, 1, 10));
        var inRange = store.AddEvent(UserId, new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 2));
        store.AddEvent(UserId, new DateOnly(2026, 6, 10));

        var document = await CreateService(store).ExportAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));

        Assert.Equal(inRange.StartDate, Assert.Single(document.Events).StartDate);
    }

    [Fact]
    public async Task ExportAsync_JsonOmitsEventAndSessionIdentifiers()
    {
        var store = new InMemoryStore();
        var own = store.AddEvent(UserId, new DateOnly(2026, 5, 1));
        store.AddSession(own, "Talk A");

        using var stream = new MemoryStream();
        await CreateService(store).ExportAsync(stream, null, null);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);

        var exported = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
        Assert.False(exported.TryGetProperty("id", out _));
        var session = Assert.Single(exported.GetProperty("sessions").EnumerateArray());
        Assert.False(session.TryGetProperty("id", out _));
        Assert.Equal("Talk A", session.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ExportAsync_IncludesTransferableFeedbackAndSettingsWithoutInternalData()
    {
        var store = new InMemoryStore();
        var parent = store.AddEvent(UserId, new DateOnly(2026, 5, 1));
        var session = store.AddSession(parent, "Talk A");
        var timestamp = DateTimeOffset.Parse("2026-04-15T10:30:00+02:00");
        store.Settings.Add(new FeedbackSettings
        {
            SessionId = session.Id,
            EventId = parent.Id,
            ValidFrom = DateTimeOffset.Parse("2026-04-15T08:00:00Z"),
            ValidTo = DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            TimeZoneId = "Europe/Rome"
        });
        store.Feedback.Add(new SessionFeedback
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            SessionRating = 8,
            SpeakerRating = 9,
            Comment = "Clear and engaging.",
            Timestamp = timestamp,
            Metadata = new SessionFeedbackMetadata { IpAddress = "192.0.2.1", UserAgent = "Test browser" }
        });

        using var stream = new MemoryStream();
        await CreateService(store).ExportAsync(stream, null, null);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);

        var exportedSession = Assert.Single(Assert.Single(
            document.RootElement.GetProperty("events").EnumerateArray()).GetProperty("sessions").EnumerateArray());
        var exportedSettings = exportedSession.GetProperty("feedbackSettings");
        Assert.Equal("2026-04-15T08:00:00+00:00", exportedSettings.GetProperty("validFrom").GetString());
        Assert.Equal("Europe/Rome", exportedSettings.GetProperty("timeZoneId").GetString());
        Assert.False(exportedSettings.TryGetProperty("eventId", out _));
        var exportedFeedback = Assert.Single(exportedSession.GetProperty("feedback").EnumerateArray());
        Assert.Equal(8, exportedFeedback.GetProperty("sessionRating").GetInt32());
        Assert.Equal(9, exportedFeedback.GetProperty("speakerRating").GetInt32());
        Assert.Equal("Clear and engaging.", exportedFeedback.GetProperty("comment").GetString());
        Assert.Equal(timestamp, exportedFeedback.GetProperty("timestamp").GetDateTimeOffset());
        Assert.False(exportedFeedback.TryGetProperty("id", out _));
        Assert.False(exportedFeedback.TryGetProperty("sessionId", out _));
        Assert.False(exportedFeedback.TryGetProperty("metadata", out _));
        Assert.DoesNotContain("192.0.2.1", Encoding.UTF8.GetString(stream.ToArray()));
        Assert.DoesNotContain("Test browser", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task ImportAsync_ProjectExampleOmitsIdentifiersAndImportsSuccessfully()
    {
        var store = new InMemoryStore();
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "example-export.json"));
        using var document = await JsonDocument.ParseAsync(stream);
        var exported = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
        Assert.False(exported.TryGetProperty("id", out _));
        Assert.False(Assert.Single(exported.GetProperty("sessions").EnumerateArray()).TryGetProperty("id", out _));
        stream.Position = 0;

        var result = await CreateService(store).ImportAsync(stream);

        Assert.Equal(new EventImportResult(1, 1, 1), result);
        var imported = Assert.Single(store.Events);
        Assert.NotEqual(Guid.Empty, imported.Id);
        var session = Assert.Single(store.Sessions);
        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(imported.Id, session.EventId);
        var settings = Assert.Single(store.Settings);
        Assert.Equal(session.Id, settings.SessionId);
        Assert.Equal(imported.Id, settings.EventId);
        var feedback = Assert.Single(store.Feedback);
        Assert.Equal(session.Id, feedback.SessionId);
        Assert.Equal(8, feedback.SessionRating);
        Assert.Null(feedback.Metadata.IpAddress);
        Assert.Null(feedback.Metadata.UserAgent);
    }

    [Fact]
    public async Task ExportAsync_RejectsInvertedRange()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService(new InMemoryStore()).ExportAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public async Task ImportAsync_RoundTripAddsCopiesWithoutTouchingExistingData()
    {
        var store = new InMemoryStore();
        var original = store.AddEvent(UserId, new DateOnly(2026, 5, 1), callForPapersDeadline: new DateOnly(2026, 3, 15));
        store.AddSession(original, "Talk A", ProposalStatus.Accepted, new DateOnly(2026, 4, 15));
        var service = CreateService(store);

        using var stream = new MemoryStream();
        await service.ExportAsync(stream, null, null);
        stream.Position = 0;
        var result = await service.ImportAsync(stream);

        Assert.Equal(new EventImportResult(1, 1), result);
        Assert.Equal(2, store.Events.Count);
        Assert.Contains(store.Events, value => value.Id == original.Id);
        var imported = Assert.Single(store.Events, value => value.Id != original.Id);
        Assert.Equal(UserId, imported.OwnerId);
        Assert.Equal(original.CallForPapersDeadline, imported.CallForPapersDeadline);
        var session = Assert.Single(store.Sessions, value => value.EventId == imported.Id);
        Assert.NotEqual(Guid.Empty, imported.Id);
        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.DoesNotContain(store.Sessions, value => value.EventId == original.Id && value.Id == session.Id);
        Assert.Equal(ProposalStatus.Accepted, session.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), session.SubmissionDate);
    }

    [Fact]
    public async Task ImportAsync_RemapsFeedbackIdsAndSettingsAndPreservesTransferableValues()
    {
        var store = new InMemoryStore();
        var originalEvent = store.AddEvent(UserId, new DateOnly(2026, 5, 1));
        var originalSession = store.AddSession(originalEvent, "Talk A");
        var timestamp1 = DateTimeOffset.Parse("2026-04-15T08:00:00Z");
        var timestamp2 = DateTimeOffset.Parse("2026-04-15T09:00:00Z");
        store.Settings.Add(new FeedbackSettings
        {
            SessionId = originalSession.Id,
            EventId = originalEvent.Id,
            ValidFrom = timestamp1,
            ValidTo = timestamp2.AddHours(2),
            TimeZoneId = "Europe/Rome"
        });
        store.Feedback.AddRange(
        [
            new SessionFeedback
            {
                Id = Guid.NewGuid(), SessionId = originalSession.Id, SessionRating = 8,
                SpeakerRating = 9, Comment = "Useful", Timestamp = timestamp1,
                Metadata = new SessionFeedbackMetadata { IpAddress = "192.0.2.1", UserAgent = "Private" }
            },
            new SessionFeedback
            {
                Id = Guid.NewGuid(), SessionId = originalSession.Id, SessionRating = 7,
                SpeakerRating = 8, Comment = null, Timestamp = timestamp2,
                Metadata = new SessionFeedbackMetadata { IpAddress = "198.51.100.1", UserAgent = "Private" }
            }
        ]);
        var service = CreateService(store);
        var document = await service.ExportAsync(null, null);

        var result = await service.ImportAsync(document);

        Assert.Equal(new EventImportResult(1, 1, 2), result);
        var importedEvent = Assert.Single(store.Events, item => item.Id != originalEvent.Id);
        var importedSession = Assert.Single(store.Sessions, item => item.EventId == importedEvent.Id);
        var importedSettings = Assert.Single(store.Settings, item => item.SessionId == importedSession.Id);
        Assert.Equal(importedEvent.Id, importedSettings.EventId);
        Assert.Equal(importedSession.Id, importedSettings.SessionId);
        Assert.Equal(timestamp1, importedSettings.ValidFrom);
        Assert.Equal("Europe/Rome", importedSettings.TimeZoneId);
        var originalFeedbackIds = store.Feedback.Where(item => item.SessionId == originalSession.Id)
            .Select(item => item.Id).ToHashSet();
        var importedFeedback = store.Feedback.Where(item => item.SessionId == importedSession.Id)
            .OrderBy(item => item.Timestamp).ToArray();
        Assert.Equal(2, importedFeedback.Length);
        Assert.All(importedFeedback, item => Assert.NotEqual(Guid.Empty, item.Id));
        Assert.All(importedFeedback, item => Assert.DoesNotContain(item.Id, originalFeedbackIds));
        Assert.Equal([timestamp1, timestamp2], importedFeedback.Select(item => item.Timestamp));
        Assert.Equal(["Useful", null], importedFeedback.Select(item => item.Comment));
        Assert.All(importedFeedback, item =>
        {
            Assert.Null(item.Metadata.IpAddress);
            Assert.Null(item.Metadata.UserAgent);
        });
    }

    [Fact]
    public async Task ImportAsync_LegacyVersionOneWithoutFeedbackRemainsSupported()
    {
        var store = new InMemoryStore();
        var legacy = new EventsExportDocument
        {
            SchemaVersion = 1,
            Events =
            [
                new ExportedEvent
                {
                    Name = "Legacy", Location = "Milan",
                    StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 5, 1),
                    Sessions = [new ExportedSession { Title = "Legacy session" }]
                }
            ]
        };

        var result = await CreateService(store).ImportAsync(legacy);

        Assert.Equal(new EventImportResult(1, 1), result);
        Assert.Empty(store.Settings);
        Assert.Empty(store.Feedback);
    }

    [Fact]
    public async Task ImportAsync_InvalidNestedFeedbackWritesNothing()
    {
        var store = new InMemoryStore();
        var document = new EventsExportDocument
        {
            Events =
            [
                new ExportedEvent
                {
                    Name = "Conference", Location = "Milan",
                    StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 5, 1),
                    Sessions =
                    [
                        new ExportedSession
                        {
                            Title = "Talk",
                            FeedbackSettings = new ExportedFeedbackSettings
                            {
                                ValidFrom = DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
                                ValidTo = DateTimeOffset.Parse("2026-05-01T09:00:00Z"),
                                TimeZoneId = "UTC"
                            },
                            Feedback = [new ExportedFeedback { SessionRating = 0, SpeakerRating = 5 }]
                        }
                    ]
                }
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateService(store).ImportAsync(document));

        Assert.Empty(store.Events);
        Assert.Empty(store.Sessions);
        Assert.Empty(store.Settings);
        Assert.Empty(store.Feedback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_AssignsNewIdentifiersAndCurrentUser(bool includeLegacyIds)
    {
        var store = new InMemoryStore();
        var legacyId = Guid.NewGuid();
        var idProperty = includeLegacyIds ? $"\"id\": \"{legacyId}\"," : string.Empty;
        var json = $$"""
            {
              "schemaVersion": 1,
              "events": [
                {
                  {{idProperty}}
                  "name": "Conf",
                  "location": "Milan",
                  "startDate": "2026-05-01",
                  "endDate": "2026-05-02",
                  "sessions": [ { {{idProperty}} "title": "Talk", "abstract": "Abstract", "status": "Rejected" } ]
                }
              ]
            }
            """;

        await CreateService(store).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        var imported = Assert.Single(store.Events);
        Assert.NotEqual(Guid.Empty, imported.Id);
        Assert.NotEqual(legacyId, imported.Id);
        Assert.Equal(UserId, imported.OwnerId);
        Assert.Null(imported.CallForPapersDeadline);
        var session = Assert.Single(store.Sessions);
        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.NotEqual(legacyId, session.Id);
        Assert.Equal(imported.Id, session.EventId);
        Assert.Equal(UserId, session.OwnerId);
        Assert.Equal(ProposalStatus.Rejected, session.Status);
        Assert.Null(session.SubmissionDate);
    }

    [Fact]
    public async Task ImportAsync_InvalidDataWritesNothing()
    {
        var store = new InMemoryStore();
        var document = new EventsExportDocument
        {
            Events =
            [
                new ExportedEvent { Name = "Valid", Location = "Milan", StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 5, 1) },
                new ExportedEvent { Name = "Invalid", Location = "Rome", StartDate = new DateOnly(2026, 5, 2), EndDate = new DateOnly(2026, 5, 1) }
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateService(store).ImportAsync(document));
        Assert.Empty(store.Events);
    }

    [Fact]
    public async Task ImportAsync_RejectsMalformedJson()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(new InMemoryStore()).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes("{ not json"))));
    }

    private static EventImportExportService CreateService(InMemoryStore store) =>
        new(store, store, new FixedUser(UserId), store);

    private sealed class FixedUser(string userId) : ICurrentUserContext
    {
        public string UserId { get; } = userId;
    }

    private sealed class InMemoryStore : IEventRepository, ISessionProposalRepository, IFeedbackService
    {
        public List<Event> Events { get; } = [];

        public List<SessionProposal> Sessions { get; } = [];

        public List<FeedbackSettings> Settings { get; } = [];

        public List<SessionFeedback> Feedback { get; } = [];

        public Event AddEvent(string ownerId, DateOnly start, DateOnly? end = null, DateOnly? callForPapersDeadline = null)
        {
            var value = new Event
            {
                OwnerId = ownerId,
                Name = "Event",
                Location = "Milan",
                StartDate = start,
                EndDate = end ?? start,
                CallForPapersDeadline = callForPapersDeadline
            };
            this.Events.Add(value);
            return value;
        }

        public SessionProposal AddSession(
            Event parent,
            string title,
            ProposalStatus status = ProposalStatus.Submitted,
            DateOnly? submissionDate = null)
        {
            var session = new SessionProposal
            {
                EventId = parent.Id,
                OwnerId = parent.OwnerId,
                Title = title,
                Abstract = "Abstract",
                Status = status,
                SubmissionDate = submissionDate
            };
            this.Sessions.Add(session);
            return session;
        }

        public Task<PagedResult<Event>> SearchAsync(string ownerId, EventSearchCriteria criteria, CancellationToken cancellationToken = default)
        {
            var owned = this.Events.Where(value => value.OwnerId == ownerId).ToArray();
            var items = owned.Skip((criteria.Page - 1) * criteria.PageSize).Take(criteria.PageSize).ToArray();
            return Task.FromResult(new PagedResult<Event>(items, criteria.Page, criteria.PageSize, owned.Length));
        }

        public Task<Event?> GetAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.Events.SingleOrDefault(value => value.OwnerId == ownerId && value.Id == eventId));

        public Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default)
        {
            this.Events.Add(value);
            return Task.FromResult(value);
        }

        public Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionProposal>> ListByEventAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionProposal>>(
                this.Sessions.Where(value => value.OwnerId == ownerId && value.EventId == eventId).ToArray());

        public Task<SessionProposal?> GetAsync(string ownerId, Guid eventId, Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SessionProposal> CreateAsync(SessionProposal proposal, CancellationToken cancellationToken = default)
        {
            this.Sessions.Add(proposal);
            return Task.FromResult(proposal);
        }

        public Task<SessionProposal?> UpdateAsync(SessionProposal proposal, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string ownerId, Guid eventId, Guid proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(string? EventTitle, string? SessionTitle)> GetSessionTitlesAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var session = this.Sessions.SingleOrDefault(value => value.Id == sessionId);
            var eventTitle = session is null ? null : this.Events.SingleOrDefault(value => value.Id == session.EventId)?.Name;
            return Task.FromResult((eventTitle, session?.Title));
        }

        public Task<FeedbackSettings?> GetSettingsAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.Settings.SingleOrDefault(value => value.SessionId == sessionId));

        public Task<FeedbackSettings> SaveSettingsAsync(FeedbackSettings settings, CancellationToken cancellationToken = default)
        {
            this.Settings.RemoveAll(value => value.SessionId == settings.SessionId);
            this.Settings.Add(settings);
            return Task.FromResult(settings);
        }

        public Task<IReadOnlyList<SessionFeedback>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionFeedback>>(this.Feedback
                .Where(value => value.SessionId == sessionId)
                .OrderByDescending(value => value.Timestamp)
                .ToArray());

        public Task<SessionFeedback> CreateAsync(
            Guid sessionId,
            SessionFeedbackSubmission submission,
            SessionFeedbackMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            var value = new SessionFeedback
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                SessionRating = submission.SessionRating,
                SpeakerRating = submission.SpeakerRating,
                Comment = submission.Comment,
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = metadata
            };
            this.Feedback.Add(value);
            return Task.FromResult(value);
        }

        public async Task ImportSessionAsync(
            Guid sessionId,
            FeedbackSettings? settings,
            IReadOnlyList<SessionFeedbackImport> feedback,
            CancellationToken cancellationToken = default)
        {
            if (settings is not null)
            {
                await this.SaveSettingsAsync(settings, cancellationToken);
            }

            foreach (var item in feedback)
            {
                this.Feedback.Add(new SessionFeedback
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    SessionRating = item.SessionRating,
                    SpeakerRating = item.SpeakerRating,
                    Comment = item.Comment,
                    Timestamp = item.Timestamp,
                    Metadata = new SessionFeedbackMetadata()
                });
            }
        }
    }
}
