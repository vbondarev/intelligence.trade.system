namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Корневой агрегат рыночного снимка для одного инструмента в конкретный момент времени.
/// Содержит полный набор данных (цена, деривативы, стакан, поток сделок, мультифреймовый
/// технический анализ, сентимент и портфель), необходимых для отправки в OpenAI GPT
/// с целью получения торговой аналитики.
/// </summary>
public sealed record MarketAnalysisSnapshot
{
    /// <summary>Название биржи, с которой собраны данные. Например: <c>Bybit</c>.</summary>
    public required string Exchange { get; init; }

    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Категория рынка инструмента.
    /// Возможные значения: <c>linear</c>, <c>spot</c>, <c>inverse</c>.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>Момент времени (UTC), в который был собран снимок.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>Текущее состояние цены: last/mark/index, bid/ask, спред, статистика за 24 ч.</summary>
    public required PriceSnapshot Price { get; init; }

    /// <summary>
    /// Данные деривативного рынка: ставка финансирования, открытый интерес,
    /// соотношение лонг/шорт.
    /// </summary>
    public required DerivativesSnapshot Derivatives { get; init; }

    /// <summary>Агрегированное состояние стакана заявок на момент снимка.</summary>
    public required OrderBookSnapshot OrderBook { get; init; }

    /// <summary>Поток совершённых сделок за скользящее временное окно.</summary>
    public required TradeFlowSnapshot TradeFlow { get; init; }

    /// <summary>Технический анализ на таймфрейме 15 минут.</summary>
    public required TimeframeAnalysisSnapshot M15 { get; init; }

    /// <summary>Технический анализ на таймфрейме 1 час.</summary>
    public required TimeframeAnalysisSnapshot H1 { get; init; }

    /// <summary>Технический анализ на таймфрейме 4 часа.</summary>
    public required TimeframeAnalysisSnapshot H4 { get; init; }

    /// <summary>Технический анализ на дневном таймфрейме.</summary>
    public required TimeframeAnalysisSnapshot D1 { get; init; }

    /// <summary>
    /// Агрегированные оценки настроения рынка, вычисленные на основе нескольких сигналов.
    /// </summary>
    public required SentimentSnapshot Sentiment { get; init; }

    /// <summary>Текущее состояние торгового счёта и открытых позиций.</summary>
    public required PortfolioSnapshot Portfolio { get; init; }

    /// <summary>
    /// Произвольные теги для классификации снимка.
    /// Например: <c>high-volatility</c>, <c>funding-spike</c>, <c>breakout</c>.
    /// </summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>
    /// Агрегированные диагностические записи для всех индикаторов всех таймфреймов.
    /// Пустой список означает, что все индикаторы рассчитаны полноценно.
    /// </summary>
    public IReadOnlyList<IndicatorDiagnosticSnapshot> IndicatorDiagnostics { get; init; } = [];
}
