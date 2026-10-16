namespace TalksOps.AI;

/// <summary>Extracts event information from a call-for-papers page.</summary>
public interface IEventDataExtractionService
{
    /// <summary>Fetches and extracts event details while recording non-content usage metadata.</summary>
    Task<ExtractedEventData> ExtractAsync(
        string sourcePageUrl,
        string userId,
        CancellationToken cancellationToken = default);
}