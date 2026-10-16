namespace TalksOps.ApiClient.Contracts;

/// <summary>Structured error returned by the TalksOps API.</summary>
public sealed record ApiErrorDto(string? Code, string? Message);