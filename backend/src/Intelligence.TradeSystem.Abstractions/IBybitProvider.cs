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

    /// <summary>
    /// Возвращает исторический ряд открытого интереса (<c>/v5/market/open-interest</c>).
    /// </summary>
    /// <param name="interval">Интервал агрегации каждой точки ряда.</param>
    /// <param name="limit">
    /// Количество точек. По умолчанию 48 — при интервале <see cref="OpenInterestInterval.FiveMinutes"/>
    /// покрывает 4 часа истории, достаточно для расчёта <c>Change1hPct</c> и <c>Change4hPct</c>.
    /// Bybit возвращает до 200 записей за запрос.
    /// </param>
    /// <returns>
    /// Список доменных моделей <see cref="OpenInterestEntry"/>;
    /// пустой список (<c>[]</c>) если запрос завершился ошибкой.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="category"/> равен <see cref="MarketCategory.Spot"/> —
    /// Bybit не предоставляет данные OI для спотового рынка.
    /// </exception>
    Task<IReadOnlyList<OpenInterestEntry>> GetOpenInterestHistoryAsync(
        string symbol,
        MarketCategory category,
        OpenInterestInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 48,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает историю ставки финансирования (<c>/v5/market/funding/history</c>).
    /// </summary>
    /// <param name="limit">
    /// Количество записей. По умолчанию 30 — покрывает ~10 дней
    /// (3 начисления в сутки × 10 дней), достаточно для расчёта <c>Avg7dRate</c>.
    /// Bybit возвращает до 200 записей за запрос.
    /// </param>
    /// <returns>
    /// Список доменных моделей <see cref="FundingRateEntry"/>;
    /// пустой список (<c>[]</c>) если запрос завершился ошибкой.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="category"/> равен <see cref="MarketCategory.Spot"/> —
    /// Bybit не предоставляет ставку финансирования для спотового рынка.
    /// </exception>
    Task<IReadOnlyList<FundingRateEntry>> GetFundingRateHistoryAsync(
        string symbol,
        MarketCategory category,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 30,
        CancellationToken cancellationToken = default);
}

