using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application;

/// <summary>
/// Собирает полный пакет публичных сырых рыночных данных по инструменту.
/// </summary>
public interface IPublicMarketDataCollector
{
    Task<CollectedPublicMarketData> CollectAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);
}
