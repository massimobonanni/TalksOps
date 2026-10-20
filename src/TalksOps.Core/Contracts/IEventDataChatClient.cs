using TalksOps.Core.Extraction;

namespace TalksOps.Core.Contracts;

/// <summary>Generates structured event data from a prepared extraction prompt.</summary>
public interface IEventDataChatClient
{
    /// <summary>Gets the configured model deployment name.</summary>
    string DeploymentName { get; }

    /// <summary>Sends the prompt to the configured model deployment.</summary>
    Task<EventDataModelResponse> CompleteAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}