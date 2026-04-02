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

    /// <summary>
    /// Возвращает снимок стакана заявок (<c>/v5/market/orderbook</c>).
    /// </summary>
    /// <param name="limit">Глубина стакана. Default 50 покрывает расчёты до Top20.</param>
    /// <returns>
    /// Доменную модель <see cref="OrderBook"/> с сырыми уровнями,
    /// либо <c>null</c> если запрос завершился ошибкой.
    /// </returns>
    Task<OrderBook?> GetOrderBookAsync(
        string symbol,
        MarketCategory category,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает список последних совершённых сделок (<c>/v5/market/recent-trade</c>).
    /// </summary>
    /// <param name="limit">
    /// Количество последних сделок. По умолчанию 60 — достаточно для статистически
    /// значимой дельты объёма. Bybit поддерживает до 1000.
    /// </param>
    /// <returns>
    /// Список доменных моделей <see cref="Trade"/>;
    /// пустой список (<c>[]</c>) если запрос завершился ошибкой.
    /// </returns>
    Task<IReadOnlyList<Trade>> GetRecentTradesAsync(
        string symbol,
        MarketCategory category,
        int limit = 60,
        CancellationToken cancellationToken = default);
}

