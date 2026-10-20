namespace TalksOps.AI;

/// <summary>Identifies a safe, user-facing failure category for event extraction.</summary>
public enum EventDataExtractionErrorCode
{
    InvalidSourceUrl,
    UnsafeSourceAddress,
    FetchFailed,
    TooManyRedirects,
    PageTooLarge,
    InvalidModelResponse,
    NoEventData
}