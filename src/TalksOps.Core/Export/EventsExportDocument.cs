namespace TalksOps.Core.Export;

/// <summary>Root of the JSON document produced by an export and consumed by an import.</summary>
/// <example>
/// <code>
/// {
///   "schemaVersion": 1,
///   "exportedAt": "2026-10-09T10:00:00+00:00",
///   "from": "2026-01-01",
///   "to": null,
///   "events": [ { "name": "Tech conference", "sessions": [ { "title": "..." } ] } ]
/// }
/// </code>
/// </example>
public sealed record EventsExportDocument
{
    /// <summary>The schema version written by the current implementation.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the version of the document format, used to reject unsupported files on import.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the UTC timestamp at which the export was produced.</summary>
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the inclusive lower bound of the exported range, or <see langword="null"/> when unbounded.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Gets the inclusive upper bound of the exported range, or <see langword="null"/> when unbounded.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Gets the exported events, each containing its sessions.</summary>
    public IReadOnlyList<ExportedEvent> Events { get; init; } = [];
}
