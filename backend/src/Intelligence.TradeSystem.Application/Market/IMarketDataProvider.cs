using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Нейтральный контракт доступа к публичным рыночным данным биржи.
/// </summary>
public interface IMarketDataProvider
{
    Task<IReadOnlyList<Kline>> GetKlinesAsync(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<Ticker?> GetTickerAsync(
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);

    Task<OrderBook?> GetOrderBookAsync(
        string symbol,
        MarketCategory category,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Trade>> GetRecentTradesAsync(
        string symbol,
        MarketCategory category,
        int limit = 60,
        CancellationToken cancellationToken = default);
}
