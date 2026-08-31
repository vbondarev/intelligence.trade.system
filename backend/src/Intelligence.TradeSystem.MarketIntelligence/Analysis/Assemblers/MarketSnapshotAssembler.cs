using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;

/// <summary>
/// Финальный оркестратор слоя Analysis.
/// Собирает корневой <see cref="MarketSnapshot"/> из уже вычисленных частичных снапшотов,
/// не выполняя никаких повторных расчётов.
/// </summary>
public static class MarketSnapshotAssembler
{
    public static MarketSnapshot Assemble(
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
        SentimentSnapshot sentiment)
    {
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

        var categoryString = category.ToString().ToLowerInvariant();
        var tags = MarketTagsBuilder.Build(derivatives, orderBook, tradeFlow, sentiment, price, m15, h1, h4);

        var allDiagnostics = new List<IndicatorDiagnosticSnapshot>(
            m15.IndicatorDiagnostics.Count +
            h1.IndicatorDiagnostics.Count +
            h4.IndicatorDiagnostics.Count +
            d1.IndicatorDiagnostics.Count);

        allDiagnostics.AddRange(m15.IndicatorDiagnostics);
        allDiagnostics.AddRange(h1.IndicatorDiagnostics);
        allDiagnostics.AddRange(h4.IndicatorDiagnostics);
        allDiagnostics.AddRange(d1.IndicatorDiagnostics);

        return new MarketSnapshot
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
            Tags = tags,
            IndicatorDiagnostics = allDiagnostics,
        };
    }
}
