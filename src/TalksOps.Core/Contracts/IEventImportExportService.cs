using TalksOps.Core.Export;

namespace TalksOps.Core.Contracts;

/// <summary>Exports and imports the current user's events together with their sessions.</summary>
public interface IEventImportExportService
{
    /// <summary>Builds an export of the current user's events overlapping the optional date range.</summary>
    /// <param name="from">Inclusive lower bound; <see langword="null"/> means no lower bound.</param>
    /// <param name="to">Inclusive upper bound; <see langword="null"/> means no upper bound.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="from"/> is after <paramref name="to"/>.</exception>
    Task<EventsExportDocument> ExportAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    /// <summary>Writes an export of the current user's events as JSON to <paramref name="destination"/>.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="from"/> is after <paramref name="to"/>.</exception>
    Task ExportAsync(
        Stream destination,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    /// <summary>Adds the events and sessions in <paramref name="document"/> to the current user's data.</summary>
    /// <remarks>Existing data is never updated or deleted; imported items always receive new identifiers.</remarks>
    /// <exception cref="InvalidDataException">Thrown when the document is unsupported or contains invalid data.</exception>
    Task<EventImportResult> ImportAsync(
        EventsExportDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a JSON export from <paramref name="source"/> and adds its content to the current user's data.</summary>
    /// <exception cref="InvalidDataException">Thrown when the stream is not a valid export.</exception>
    Task<EventImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
