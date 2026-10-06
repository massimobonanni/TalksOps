namespace TalksOps.ApiClient.Contracts;

/// <summary>REST representation of a page of results.</summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);