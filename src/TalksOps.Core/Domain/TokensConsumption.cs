namespace TalksOps.Core.Domain;

/// <summary>Represents token consumption for an event extraction call.</summary>
public sealed record TokensConsumption
{
    /// <summary>Gets the identifier of the user who initiated the extraction.</summary>
    public required string UserId { get; init; }

    /// <summary>Gets the UTC timestamp when the model call completed.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the name of the model deployment used for the call.</summary>
    public required string DeploymentName { get; init; }

    /// <summary>Gets the number of input tokens, when reported by the model client.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Gets the number of output tokens, when reported by the model client.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Gets the call-for-papers URL used for the extraction.</summary>
    public required string CallForPapersUrl { get; init; }
}
