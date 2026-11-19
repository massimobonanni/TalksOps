using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Services;
using TalksOps.Functions;

namespace TalksOps.Functions.Tests;

public sealed class RequestScopedServicesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("  Bring the demo.\nRoom confirmed.")]
    public async Task CreateEvent_AcceptsOptionalNotesAndReturnsThemWhenPresent(string? notes)
    {
        var currentUser = new RequestCurrentUserContext();
        var repository = new FakeEventRepository();
        var functions = new EventsFunctions(
            new EventService(repository, currentUser),
            new SessionProposalService(new FakeProposalRepository(new SessionProposal
            {
                EventId = Guid.NewGuid(),
                OwnerId = "authenticated-sub",
                Title = "Unused proposal"
            }), currentUser),
            currentUser);
        var request = new SaveEventRequest(
            "Conference", "Milan", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2),
            new Dictionary<string, decimal>())
        {
            Notes = notes
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(new ApiRequest<SaveEventRequest>("authenticated-sub", request), options);
        using var requestJson = JsonDocument.Parse(json);
        Assert.Equal(notes is not null, requestJson.RootElement.GetProperty("payload").TryGetProperty("notes", out _));
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var httpRequest = new DefaultHttpContext().Request;
        httpRequest.ContentType = "application/json";
        httpRequest.Body = body;

        var result = Assert.IsType<CreatedResult>(await functions.CreateEvent(httpRequest, default));
        var response = Assert.IsType<EventDto>(result.Value);
        Assert.Equal(notes, repository.CreatedEvent!.Notes);
        Assert.Equal(notes, response.Notes);

        using var responseJson = JsonSerializer.SerializeToDocument(response, options);
        Assert.Equal(notes is not null, responseJson.RootElement.TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task EventService_UsesRequestUserInsteadOfSubmittedOwner()
    {
        var currentUser = CreateCurrentUser();
        var repository = new FakeEventRepository();
        var service = new EventService(repository, currentUser);

        await service.CreateAsync(new Event
        {
            OwnerId = "untrusted-owner",
            Name = "Conference",
            Location = "Milan",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 2)
        });

        Assert.Equal("authenticated-sub", repository.CreatedEvent!.OwnerId);
    }

    [Fact]
    public async Task EventService_ScopesSearchUpdateAndDeleteToRequestUser()
    {
        var currentUser = CreateCurrentUser();
        var repository = new FakeEventRepository();
        var service = new EventService(repository, currentUser);
        var value = new Event
        {
            OwnerId = "untrusted-owner",
            Name = "Conference",
            Location = "Milan",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 2)
        };

        await service.SearchAsync(new EventSearchCriteria());
        await service.UpdateAsync(value);
        await service.DeleteAsync(value.Id);

        Assert.Equal("authenticated-sub", repository.SearchOwnerId);
        Assert.Equal("authenticated-sub", repository.UpdatedEvent!.OwnerId);
        Assert.Equal("authenticated-sub", repository.DeleteOwnerId);
    }

    [Fact]
    public async Task SessionProposalService_CancelPersistsCancelledStatus()
    {
        var currentUser = CreateCurrentUser();
        var proposal = new SessionProposal
        {
            EventId = Guid.NewGuid(),
            OwnerId = "authenticated-sub",
            Title = "Reliable systems",
            Abstract = "A proposal abstract",
        };
        var repository = new FakeProposalRepository(proposal);
        var service = new SessionProposalService(repository, currentUser);

        var cancelled = await service.CancelAsync(proposal.EventId, proposal.Id);

        Assert.NotNull(cancelled);
        Assert.Equal(ProposalStatus.Cancelled, cancelled.Status);
        Assert.Equal(ProposalStatus.Cancelled, repository.UpdatedProposal!.Status);
    }

    [Fact]
    public async Task SessionProposalService_UsesRequestUserForCreateAndUpdate()
    {
        var currentUser = CreateCurrentUser();
        var proposal = new SessionProposal
        {
            EventId = Guid.NewGuid(),
            OwnerId = "untrusted-owner",
            Title = "Reliable systems",
            Abstract = "A proposal abstract",
        };
        var repository = new FakeProposalRepository(proposal with { OwnerId = "authenticated-sub" });
        var service = new SessionProposalService(repository, currentUser);

        var created = await service.CreateAsync(proposal);
        var updated = await service.UpdateAsync(proposal with { Title = "Updated title" });

        Assert.Equal("authenticated-sub", created.OwnerId);
        Assert.Equal("authenticated-sub", updated!.OwnerId);
        Assert.Equal("authenticated-sub", repository.LastOwnerId);
    }

    private static RequestCurrentUserContext CreateCurrentUser()
    {
        var context = new RequestCurrentUserContext();
        Assert.True(context.TryInitialize("authenticated-sub"));
        return context;
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Event? CreatedEvent { get; private set; }
        public Event? UpdatedEvent { get; private set; }
        public string? SearchOwnerId { get; private set; }
        public string? DeleteOwnerId { get; private set; }

        public Task<PagedResult<Event>> SearchAsync(string ownerId, EventSearchCriteria criteria, CancellationToken cancellationToken = default)
        {
            this.SearchOwnerId = ownerId;
            return Task.FromResult(new PagedResult<Event>([], 1, 20, 0));
        }

        public Task<Event?> GetAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Event?>(null);

        public Task<Event> CreateAsync(Event value, CancellationToken cancellationToken = default)
        {
            this.CreatedEvent = value;
            return Task.FromResult(value);
        }

        public Task<Event?> UpdateAsync(Event value, CancellationToken cancellationToken = default)
        {
            this.UpdatedEvent = value;
            return Task.FromResult<Event?>(value);
        }

        public Task<bool> DeleteAsync(string ownerId, Guid eventId, CancellationToken cancellationToken = default)
        {
            this.DeleteOwnerId = ownerId;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeProposalRepository(SessionProposal proposal) : ISessionProposalRepository
    {
        public SessionProposal? UpdatedProposal { get; private set; }
        public string? LastOwnerId { get; private set; }

        public Task<IReadOnlyList<SessionProposal>> ListByEventAsync(
            string ownerId,
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            this.LastOwnerId = ownerId;
            return Task.FromResult<IReadOnlyList<SessionProposal>>([]);
        }

        public Task<SessionProposal?> GetAsync(
            string ownerId,
            Guid eventId,
            Guid proposalId,
            CancellationToken cancellationToken = default)
        {
            this.LastOwnerId = ownerId;
            return Task.FromResult<SessionProposal?>(
                ownerId == proposal.OwnerId && eventId == proposal.EventId && proposalId == proposal.Id
                    ? this.UpdatedProposal ?? proposal
                    : null);
        }

        public Task<SessionProposal> CreateAsync(
            SessionProposal value,
            CancellationToken cancellationToken = default)
        {
            this.LastOwnerId = value.OwnerId;
            return Task.FromResult(value);
        }

        public Task<SessionProposal?> UpdateAsync(
            SessionProposal value,
            CancellationToken cancellationToken = default)
        {
            this.LastOwnerId = value.OwnerId;
            this.UpdatedProposal = value;
            return Task.FromResult<SessionProposal?>(value);
        }

        public Task<bool> DeleteAsync(
            string ownerId,
            Guid eventId,
            Guid proposalId,
            CancellationToken cancellationToken = default)
        {
            this.LastOwnerId = ownerId;
            return Task.FromResult(ownerId == proposal.OwnerId && eventId == proposal.EventId && proposalId == proposal.Id);
        }
    }
}