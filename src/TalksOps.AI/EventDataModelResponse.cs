namespace TalksOps.AI;

/// <summary>Contains the model output and the usage metadata returned by Foundry.</summary>
public sealed record EventDataModelResponse(
    string? Content,
    string DeploymentName,
    int? InputTokens,
    int? OutputTokens);