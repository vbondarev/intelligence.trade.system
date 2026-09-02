using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Нормализованный пакет сырых публичных рыночных данных,
/// собранных для одного инструмента и одной биржи.
/// </summary>
public sealed record CollectedPublicMarketData
{
    /// <summary>Идентификатор биржи, с которой собран пакет данных.</summary>
    public required ExchangeId ExchangeId { get; init; }

    /// <summary>Тикер инструмента. Например: <c>BTCUSDT</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка инструмента.</summary>
    public required MarketCategory Category { get; init; }

    /// <summary>
    /// Сырые данные тикера.
    /// Может быть <c>null</c>, если запрос завершился ошибкой; дальнейший orchestration-слой
    /// обязан явно валидировать это как отсутствие критичных данных.
    /// </summary>
    public Ticker? Ticker { get; init; }

    /// <summary>
    /// Сырой снимок стакана заявок.
    /// Может быть <c>null</c>, если запрос завершился ошибкой.
    /// </summary>
    public OrderBook? OrderBook { get; init; }

    /// <summary>
    /// Последние совершённые сделки.
    /// Пустой список означает отсутствие доступных данных или ошибку запроса.
    /// </summary>
    public IReadOnlyList<Trade> Trades { get; init; } = [];

    /// <summary>Свечи таймфрейма 15 минут.</summary>
    public IReadOnlyList<Kline> M15Klines { get; init; } = [];

    /// <summary>Свечи таймфрейма 1 час.</summary>
    public IReadOnlyList<Kline> H1Klines { get; init; } = [];

    /// <summary>Свечи таймфрейма 4 часа.</summary>
    public IReadOnlyList<Kline> H4Klines { get; init; } = [];

    /// <summary>Свечи дневного таймфрейма.</summary>
    public IReadOnlyList<Kline> D1Klines { get; init; } = [];

    /// <summary>
    /// История открытого интереса.
    /// Для рынков без OI или при ошибке запроса возвращается пустой список.
    /// </summary>
    public IReadOnlyList<OpenInterestEntry> OpenInterestEntries { get; init; } = [];

    /// <summary>
    /// Интервал агрегации истории открытого интереса,
    /// с которым был собран <see cref="OpenInterestEntries"/>.
    /// </summary>
    public OpenInterestInterval OpenInterestInterval { get; init; } = OpenInterestInterval.FiveMinutes;

    /// <summary>
    /// История ставок финансирования.
    /// Для рынков без funding или при ошибке запроса возвращается пустой список.
    /// </summary>
    public IReadOnlyList<FundingRateEntry> FundingRateEntries { get; init; } = [];

    /// <summary>
    /// История соотношения лонг/шорт.
    /// Для рынков без этих данных или при ошибке запроса возвращается пустой список.
    /// </summary>
    public IReadOnlyList<LongShortRatioEntry> LongShortRatioEntries { get; init; } = [];

    /// <summary>
    /// Период агрегации истории соотношения лонг/шорт,
    /// с которым был собран <see cref="LongShortRatioEntries"/>.
    /// </summary>
    public LongShortRatioPeriod LongShortRatioPeriod { get; init; } = LongShortRatioPeriod.FiveMinutes;
}
