using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Builds a public market snapshot without owning cache or request lifetime concerns.
/// </summary>
public interface IPublicMarketSnapshotBuilder
{
    Task<MarketSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);
}
