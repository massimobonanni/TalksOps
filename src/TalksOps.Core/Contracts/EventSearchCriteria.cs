namespace TalksOps.Core.Contracts;

/// <summary>Defines filters and pagination for an event search.</summary>
public sealed record EventSearchCriteria
{
    /// <summary>Gets or initializes the event year filter.</summary>
    public int? Year { get; init; }

    /// <summary>Gets or initializes the event name filter.</summary>
    public string? Name { get; init; }

    /// <summary>Gets or initializes the event location filter.</summary>
    public string? Location { get; init; }

    /// <summary>Gets or initializes the one-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Gets or initializes the maximum number of results per page.</summary>
    public int PageSize { get; init; } = 20;
}