namespace TalksOps.AI;

/// <summary>Fetches HTML from a publicly reachable HTTP(S) page.</summary>
public interface IEventPageFetcher
{
    /// <summary>Fetches a page after validating its address and any redirects.</summary>
    Task<string> FetchAsync(string sourcePageUrl, CancellationToken cancellationToken = default);
}