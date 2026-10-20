namespace TalksOps.AI;

/// <summary>Represents a classified failure while extracting event details.</summary>
public sealed class EventDataExtractionException(
    EventDataExtractionErrorCode errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Gets the extraction failure category.</summary>
    public EventDataExtractionErrorCode ErrorCode { get; } = errorCode;
}