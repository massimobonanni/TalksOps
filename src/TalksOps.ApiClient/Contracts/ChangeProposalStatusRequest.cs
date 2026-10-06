using TalksOps.Core.Domain;

namespace TalksOps.ApiClient.Contracts;

/// <summary>Fields accepted when changing a proposal's status.</summary>
public sealed record ChangeProposalStatusRequest(ProposalStatus Status);