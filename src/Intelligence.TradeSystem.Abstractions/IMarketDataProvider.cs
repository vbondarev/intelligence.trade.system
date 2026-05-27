using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Abstractions;

/// <summary>
/// Нейтральный контракт доступа к публичным рыночным данным биржи.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Возвращает исторический ряд свечей инструмента.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи, например <c>BTCUSDT</c>.</param>
    /// <param name="category">Категория рынка, к которой относится инструмент.</param>
    /// <param name="interval">Интервал агрегации каждой свечи.</param>
    /// <param name="startTime">Необязательная нижняя граница периода выборки.</param>
    /// <param name="endTime">Необязательная верхняя граница периода выборки.</param>
    /// <param name="limit">Необязательное ограничение количества возвращаемых свечей.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Список доменных моделей <see cref="Kline"/>;
    /// пустой список (<c>[]</c>) если данные недоступны или запрос завершился ошибкой.
    /// </returns>
    Task<IReadOnlyList<Kline>> GetKlinesAsync(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает текущий тикер инструмента.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи.</param>
    /// <param name="category">Категория рынка, к которой относится инструмент.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Доменную модель <see cref="Ticker"/> с сырыми рыночными данными,
    /// либо <c>null</c> если запрос завершился ошибкой.
    /// </returns>
    Task<Ticker?> GetTickerAsync(
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает снимок стакана заявок инструмента.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи.</param>
    /// <param name="category">Категория рынка, к которой относится инструмент.</param>
    /// <param name="limit">Глубина стакана, которую должна запросить реализация.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
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
    /// Возвращает список последних совершённых сделок инструмента.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи.</param>
    /// <param name="category">Категория рынка, к которой относится инструмент.</param>
    /// <param name="limit">Количество последних сделок, которое должна запросить реализация.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Список доменных моделей <see cref="Trade"/>;
    /// пустой список (<c>[]</c>) если данные недоступны или запрос завершился ошибкой.
    /// </returns>
    Task<IReadOnlyList<Trade>> GetRecentTradesAsync(
        string symbol,
        MarketCategory category,
        int limit = 60,
        CancellationToken cancellationToken = default);
}
