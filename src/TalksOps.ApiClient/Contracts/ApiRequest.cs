namespace TalksOps.ApiClient.Contracts;

/// <summary>Wraps a REST request with the server-derived user identifier.</summary>
public sealed record ApiRequest<T>(string UserId, T Payload);