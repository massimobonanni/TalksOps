using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Services;

namespace TalksOps.Core.Tests;

public sealed class SessionProposalServiceTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesAndClearsSubmissionDate()
    {
        var repository = new InMemoryProposalRepository
        {
            Proposal = new SessionProposal
            {
                EventId = Guid.NewGuid(),
                OwnerId = "user-1",
                Title = "Talk",
                SubmissionDate = new DateOnly(2026, 3, 1)
            }
        };
        var service = new SessionProposalService(repository, new FixedUser());

        var updated = await service.UpdateAsync(repository.Proposal with { SubmissionDate = new DateOnly(2026, 4, 1) });
        Assert.Equal(new DateOnly(2026, 4, 1), updated!.SubmissionDate);

        var cleared = await service.UpdateAsync(updated with { SubmissionDate = null });
        Assert.Null(cleared!.SubmissionDate);
    }

    private sealed class FixedUser : ICurrentUserContext
    {
        public string UserId => "user-1";
    }

    private sealed class InMemoryProposalRepository : ISessionProposalRepository
    {
        public required SessionProposal Proposal { get; set; }

        public Task<IReadOnlyList<SessionProposal>> ListByEventAsync(
            string ownerId,
            Guid eventId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SessionProposal?> GetAsync(
            string ownerId,
            Guid eventId,
            Guid proposalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionProposal?>(
                this.Proposal.OwnerId == ownerId && this.Proposal.EventId == eventId && this.Proposal.Id == proposalId
                    ? this.Proposal
                    : null);

        public Task<SessionProposal> CreateAsync(
            SessionProposal proposal,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SessionProposal?> UpdateAsync(
            SessionProposal proposal,
            CancellationToken cancellationToken = default)
        {
            this.Proposal = proposal;
            return Task.FromResult<SessionProposal?>(proposal);
        }

        public Task<bool> DeleteAsync(
            string ownerId,
            Guid eventId,
            Guid proposalId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}