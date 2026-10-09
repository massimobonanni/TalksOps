using System.Text;
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
        store.AddSession(own, "Talk A");

        var document = await CreateService(store).ExportAsync(null, null);

        var exported = Assert.Single(document.Events);
        Assert.Equal(own.Id, exported.Id);
        Assert.Equal("Talk A", Assert.Single(exported.Sessions).Title);
    }

    [Fact]
    public async Task ExportAsync_FiltersByOverlappingDateRange()
    {
        var store = new InMemoryStore();
        store.AddEvent(UserId, new DateOnly(2026, 1, 10));
        var inRange = store.AddEvent(UserId, new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 2));
        store.AddEvent(UserId, new DateOnly(2026, 6, 10));

        var document = await CreateService(store).ExportAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));

        Assert.Equal(inRange.Id, Assert.Single(document.Events).Id);
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
        var original = store.AddEvent(UserId, new DateOnly(2026, 5, 1));
        store.AddSession(original, "Talk A", ProposalStatus.Accepted);
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
        var session = Assert.Single(store.Sessions, value => value.EventId == imported.Id);
        Assert.Equal(ProposalStatus.Accepted, session.Status);
    }

    [Fact]
    public async Task ImportAsync_AssignsImportedDataToCurrentUser()
    {
        var store = new InMemoryStore();
        var json = """
            {
              "schemaVersion": 1,
              "events": [
                {
                  "name": "Conf",
                  "location": "Milan",
                  "startDate": "2026-05-01",
                  "endDate": "2026-05-02",
                  "sessions": [ { "title": "Talk", "abstract": "Abstract", "status": "Rejected" } ]
                }
              ]
            }
            """;

        await CreateService(store).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(UserId, Assert.Single(store.Events).OwnerId);
        Assert.Equal(ProposalStatus.Rejected, Assert.Single(store.Sessions).Status);
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
        new(store, store, new FixedUser(UserId));

    private sealed class FixedUser(string userId) : ICurrentUserContext
    {
        public string UserId { get; } = userId;
    }

    private sealed class InMemoryStore : IEventRepository, ISessionProposalRepository
    {
        public List<Event> Events { get; } = [];

        public List<SessionProposal> Sessions { get; } = [];

        public Event AddEvent(string ownerId, DateOnly start, DateOnly? end = null)
        {
            var value = new Event { OwnerId = ownerId, Name = "Event", Location = "Milan", StartDate = start, EndDate = end ?? start };
            this.Events.Add(value);
            return value;
        }

        public void AddSession(Event parent, string title, ProposalStatus status = ProposalStatus.Submitted) =>
            this.Sessions.Add(new SessionProposal { EventId = parent.Id, OwnerId = parent.OwnerId, Title = title, Abstract = "Abstract", Status = status });

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
    }
}
