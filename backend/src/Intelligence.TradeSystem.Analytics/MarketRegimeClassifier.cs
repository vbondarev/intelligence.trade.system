using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Классифицирует рыночный режим по агрегированному <see cref="MarketAnalysisSnapshot"/>.
/// Логика намеренно согласована с текущей эвристикой, используемой при сборке <see cref="SentimentSnapshot"/>,
/// чтобы analytics-layer и analysis-layer не расходились по трактовке режима рынка.
/// </summary>
public sealed class MarketRegimeClassifier : IMarketRegimeClassifier
{
    /// <inheritdoc />
    public string Classify(MarketAnalysisSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return MarketRegimePolicy.Classify(snapshot.H1, snapshot.H4);
    }
}

