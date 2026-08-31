
namespace Intelligence.TradeSystem.MarketIntelligence.Snapshots;

/// <summary>
/// Корневой агрегат публичного рыночного снимка для одного инструмента в конкретный момент времени.
/// Содержит только публичные рыночные данные и результаты детерминированного анализа.
/// </summary>
public sealed record MarketSnapshot
{
    public required string Exchange { get; init; }

    public required string Symbol { get; init; }

    public required string Category { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required PriceSnapshot Price { get; init; }

    public required DerivativesSnapshot Derivatives { get; init; }

    public required OrderBookSnapshot OrderBook { get; init; }

    public required TradeFlowSnapshot TradeFlow { get; init; }

    public required TimeframeAnalysisSnapshot M15 { get; init; }

    public required TimeframeAnalysisSnapshot H1 { get; init; }

    public required TimeframeAnalysisSnapshot H4 { get; init; }

    public required TimeframeAnalysisSnapshot D1 { get; init; }

    public required SentimentSnapshot Sentiment { get; init; }

    public List<string> Tags { get; init; } = [];

    public IReadOnlyList<IndicatorDiagnosticSnapshot> IndicatorDiagnostics { get; init; } = [];
}
