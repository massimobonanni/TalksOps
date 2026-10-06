namespace TalksOps.Core.Contracts;

/// <summary>Represents a page of query results.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);