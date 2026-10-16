namespace TalksOps.ApiClient.Contracts;

/// <summary>URL of a call-for-papers page to extract event data from.</summary>
public sealed record ExtractEventDataRequest(string? SourcePageUrl);