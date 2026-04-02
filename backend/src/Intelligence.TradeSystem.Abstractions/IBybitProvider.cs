using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Abstractions;

public interface IBybitProvider
{
    Task<IReadOnlyList<Kline>> GetKlinesAsync(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает текущий тикер инструмента (<c>/v5/market/tickers</c>).
    /// </summary>
    /// <returns>
    /// Доменную модель <see cref="Ticker"/> с сырыми данными биржи,
    /// либо <c>null</c> если запрос завершился ошибкой.
    /// </returns>
    Task<Ticker?> GetTickerAsync(
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);
}

