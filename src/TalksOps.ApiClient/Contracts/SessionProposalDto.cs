using TalksOps.Core.Domain;

namespace TalksOps.ApiClient.Contracts;

/// <summary>REST representation of a session proposal.</summary>
public sealed record SessionProposalDto(
    Guid Id,
    Guid EventId,
    string OwnerId,
    string Title,
    string Abstract,
    string Notes,
    ProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);