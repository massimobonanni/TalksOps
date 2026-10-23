using TalksOps.Core.Domain;

namespace TalksOps.Core.Contracts;

/// <summary>Defines persistence operations for model token consumption.</summary>
public interface ITokensConsumptionRepository
{
    /// <summary>Records token consumption for an extraction call.</summary>
    Task<TokensConsumption> CreateAsync(
        TokensConsumption value,
        CancellationToken cancellationToken = default);
}
