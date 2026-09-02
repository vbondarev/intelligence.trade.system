using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Оркестрирует сбор сырых данных и построение финального <see cref="MarketSnapshot"/>.
/// </summary>
public interface IMarketSnapshotService
{
    Task<MarketSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);
}
