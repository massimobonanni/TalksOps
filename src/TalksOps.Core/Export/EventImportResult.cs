namespace TalksOps.Core.Export;

/// <summary>Summarizes the outcome of an import.</summary>
/// <param name="EventsImported">Number of events added to storage.</param>
/// <param name="SessionsImported">Number of sessions added to storage.</param>
public sealed record EventImportResult(int EventsImported, int SessionsImported);
