using System.Text.Json.Serialization;
using TalksOps.Core.Domain;

namespace TalksOps.Core.Export;

/// <summary>JSON representation of an exported session proposal.</summary>
public sealed record ExportedSession
{
    /// <summary>Gets the identifier the session had in the source system.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the session title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the session abstract.</summary>
    public required string Abstract { get; init; }

    /// <summary>Gets optional notes about the session.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Gets the session status, serialized by name to keep the file readable and stable.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ProposalStatus>))]
    public ProposalStatus Status { get; init; } = ProposalStatus.Submitted;

    /// <summary>Gets the creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the last update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
