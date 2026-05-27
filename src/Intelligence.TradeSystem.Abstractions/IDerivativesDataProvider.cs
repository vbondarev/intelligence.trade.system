using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Abstractions;

/// <summary>
/// Нейтральный контракт доступа к публичным деривативным данным биржи.
/// </summary>
public interface IDerivativesDataProvider
{
    /// <summary>
    /// Возвращает исторический ряд открытого интереса инструмента.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи.</param>
    /// <param name="category">Категория рынка, для которой доступны данные открытого интереса.</param>
    /// <param name="interval">Интервал агрегации каждой точки ряда.</param>
    /// <param name="startTime">Необязательная нижняя граница периода выборки.</param>
    /// <param name="endTime">Необязательная верхняя граница периода выборки.</param>
    /// <param name="limit">Количество точек ряда, которое должна запросить реализация.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Список доменных моделей <see cref="OpenInterestEntry"/>;
    /// пустой список (<c>[]</c>) если данные недоступны или запрос завершился ошибкой.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="category"/> указывает на рынок, где открытый интерес недоступен
    /// (например, <see cref="MarketCategory.Spot"/>).
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
    /// Возвращает историю ставок финансирования инструмента.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи.</param>
    /// <param name="category">Категория рынка, для которой доступны ставки финансирования.</param>
    /// <param name="startTime">Необязательная нижняя граница периода выборки.</param>
    /// <param name="endTime">Необязательная верхняя граница периода выборки.</param>
    /// <param name="limit">Количество записей, которое должна запросить реализация.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Список доменных моделей <see cref="FundingRateEntry"/>;
    /// пустой список (<c>[]</c>) если данные недоступны или запрос завершился ошибкой.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="category"/> указывает на рынок, где ставка финансирования недоступна
    /// (например, <see cref="MarketCategory.Spot"/>).
    /// </exception>
    Task<IReadOnlyList<FundingRateEntry>> GetFundingRateHistoryAsync(
        string symbol,
        MarketCategory category,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 30,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает исторический ряд соотношения лонг/шорт позиций.
    /// </summary>
    /// <param name="symbol">Тикер инструмента в формате целевой биржи.</param>
    /// <param name="category">Категория рынка, для которой доступно соотношение лонг/шорт.</param>
    /// <param name="period">Период агрегации каждой точки ряда.</param>
    /// <param name="startTime">Необязательная нижняя граница периода выборки.</param>
    /// <param name="endTime">Необязательная верхняя граница периода выборки.</param>
    /// <param name="limit">Количество точек ряда, которое должна запросить реализация.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Список доменных моделей <see cref="LongShortRatioEntry"/>;
    /// пустой список (<c>[]</c>) если данные недоступны или запрос завершился ошибкой.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="category"/> указывает на рынок, где данные соотношения лонг/шорт недоступны
    /// (например, <see cref="MarketCategory.Spot"/>).
    /// </exception>
    Task<IReadOnlyList<LongShortRatioEntry>> GetLongShortRatioHistoryAsync(
        string symbol,
        MarketCategory category,
        LongShortRatioPeriod period,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 50,
        CancellationToken cancellationToken = default);
}
