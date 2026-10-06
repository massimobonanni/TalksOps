namespace TalksOps.ApiClient.Contracts;

/// <summary>Filters and pagination accepted by event search.</summary>
public sealed record EventSearchRequest(
    int? Year = null,
    string? Name = null,
    string? Location = null,
    int Page = 1,
    int PageSize = 20);