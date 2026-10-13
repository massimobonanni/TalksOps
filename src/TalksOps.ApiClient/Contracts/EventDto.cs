using System.Text.Json.Serialization;

namespace TalksOps.ApiClient.Contracts;

/// <summary>REST representation of an event.</summary>
public sealed record EventDto(
    Guid Id,
    string OwnerId,
    string Name,
    string Location,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyDictionary<string, decimal> Costs,
    string? OfficialWebsiteUrl = null,
    string? CallForPapersUrl = null,
    DateOnly? CallForPapersDeadline = null)
{
    /// <summary>Gets the sum of all named event costs.</summary>
    [JsonIgnore]
    public decimal TotalCosts => this.Costs.Values.Sum();
}