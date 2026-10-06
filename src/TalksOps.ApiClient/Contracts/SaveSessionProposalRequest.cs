namespace TalksOps.ApiClient.Contracts;

/// <summary>Fields accepted when creating or updating a session proposal.</summary>
public sealed record SaveSessionProposalRequest(
    string Title,
    string Abstract,
    string SpeakerName);