using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;

/// <summary>
/// Финальный оркестратор слоя Analysis.
/// Собирает корневой <see cref="MarketAnalysisSnapshot"/> из уже вычисленных частичных снапшотов,
/// не выполняя никаких повторных расчётов.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация всех обязательных параметров</item>
///   <item>Нормализация <see cref="MarketCategory"/> в строковую форму категории</item>
///   <item>Построение тегов классификации из уже собранных данных (делегируется <see cref="MarketTagsBuilder"/>)</item>
///   <item>Сборка корневого снимка с фиксацией времени сборки (<c>DateTimeOffset.UtcNow</c>)</item>
/// </list>
/// </para>
/// </summary>
public static class MarketAnalysisSnapshotAssembler
{
    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Вычисляет и возвращает <see cref="MarketAnalysisSnapshot"/> из набора готовых снапшотов.
    /// </summary>
    /// <param name="exchange">Название биржи. Например: <c>Bybit</c>.</param>
    /// <param name="symbol">Тикер инструмента. Например: <c>BTCUSDT</c>.</param>
    /// <param name="category">Категория рынка инструмента.</param>
    /// <param name="price">Снапшот текущей цены.</param>
    /// <param name="derivatives">Снапшот деривативных данных.</param>
    /// <param name="orderBook">Снапшот стакана заявок.</param>
    /// <param name="tradeFlow">Снапшот потока совершённых сделок.</param>
    /// <param name="m15">Технический анализ на таймфрейме 15 минут.</param>
    /// <param name="h1">Технический анализ на таймфрейме 1 час.</param>
    /// <param name="h4">Технический анализ на таймфрейме 4 часа.</param>
    /// <param name="d1">Технический анализ на дневном таймфрейме.</param>
    /// <param name="sentiment">Агрегированный снапшот сентимента.</param>
    /// <param name="portfolio">Снапшот торгового счёта и открытых позиций.</param>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="exchange"/> или <paramref name="symbol"/> пустые или состоят из пробелов.
    /// </exception>
    /// <exception cref="ArgumentNullException">Если любой из снапшотов равен <c>null</c>.</exception>
    public static MarketAnalysisSnapshot Assemble(
        string exchange,
        string symbol,
        MarketCategory category,
        PriceSnapshot price,
        DerivativesSnapshot derivatives,
        OrderBookSnapshot orderBook,
        TradeFlowSnapshot tradeFlow,
        TimeframeAnalysisSnapshot m15,
        TimeframeAnalysisSnapshot h1,
        TimeframeAnalysisSnapshot h4,
        TimeframeAnalysisSnapshot d1,
        SentimentSnapshot sentiment,
        PortfolioSnapshot portfolio)
    {
        // 1. Validate
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(derivatives);
        ArgumentNullException.ThrowIfNull(orderBook);
        ArgumentNullException.ThrowIfNull(tradeFlow);
        ArgumentNullException.ThrowIfNull(m15);
        ArgumentNullException.ThrowIfNull(h1);
        ArgumentNullException.ThrowIfNull(h4);
        ArgumentNullException.ThrowIfNull(d1);
        ArgumentNullException.ThrowIfNull(sentiment);
        ArgumentNullException.ThrowIfNull(portfolio);

        // 2. Category string — lowercase to match API conventions: "linear", "spot", "inverse"
        var categoryString = category.ToString().ToLowerInvariant();

        // 3. Tags — делегируется MarketTagsBuilder (V2 whitelist, приоритетный порядок, лимит 20).
        //    capturedAtUtc не передаётся: mode-specific пороги свежести tradeFlow известны только в API-слое.
        //    stale-tradeflow добавляется LlmTagEnricher после оценки health.
        var tags = MarketTagsBuilder.Build(derivatives, orderBook, tradeFlow, sentiment, price, m15, h1, h4);

        // 4. Assemble
        // Aggregate indicator diagnostics from all four timeframes in stable order.
        var allDiagnostics = new List<IndicatorDiagnosticSnapshot>(
            m15.IndicatorDiagnostics.Count +
            h1.IndicatorDiagnostics.Count +
            h4.IndicatorDiagnostics.Count +
            d1.IndicatorDiagnostics.Count);

        allDiagnostics.AddRange(m15.IndicatorDiagnostics);
        allDiagnostics.AddRange(h1.IndicatorDiagnostics);
        allDiagnostics.AddRange(h4.IndicatorDiagnostics);
        allDiagnostics.AddRange(d1.IndicatorDiagnostics);

        return new MarketAnalysisSnapshot
        {
            Exchange = exchange,
            Symbol = symbol,
            Category = categoryString,
            CapturedAtUtc = DateTimeOffset.UtcNow,

            Price = price,
            Derivatives = derivatives,
            OrderBook = orderBook,
            TradeFlow = tradeFlow,

            M15 = m15,
            H1 = h1,
            H4 = h4,
            D1 = d1,

            Sentiment = sentiment,
            Portfolio = portfolio,

            Tags = tags,
            IndicatorDiagnostics = allDiagnostics,
        };
    }
}
