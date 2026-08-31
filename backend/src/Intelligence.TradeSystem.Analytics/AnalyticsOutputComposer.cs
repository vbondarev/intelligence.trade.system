using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Тонкий orchestration-компонент аналитического слоя,
/// объединяющий классификацию рыночного режима и форматирование текстового контекста.
/// Сам не пересчитывает raw exchange data, а согласует результаты classifier и formatter
/// поверх уже готового <see cref="MarketAnalysisSnapshot"/>.
/// </summary>
public sealed class AnalyticsOutputComposer : IAnalyticsOutputComposer
{
    private readonly IMarketRegimeClassifier _marketRegimeClassifier;
    private readonly IAnalyticsFormatter _analyticsFormatter;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AnalyticsOutputComposer"/>.
    /// </summary>
    /// <param name="marketRegimeClassifier">Классификатор рыночного режима.</param>
    /// <param name="analyticsFormatter">Форматтер компактного текстового контекста.</param>
    /// <exception cref="ArgumentNullException">
    /// Если <paramref name="marketRegimeClassifier"/> или <paramref name="analyticsFormatter"/> равен <c>null</c>.
    /// </exception>
    public AnalyticsOutputComposer(
        IMarketRegimeClassifier marketRegimeClassifier,
        IAnalyticsFormatter analyticsFormatter)
    {
        _marketRegimeClassifier = marketRegimeClassifier ?? throw new ArgumentNullException(nameof(marketRegimeClassifier));
        _analyticsFormatter = analyticsFormatter ?? throw new ArgumentNullException(nameof(analyticsFormatter));
    }

    /// <inheritdoc />
    public AnalyticsOutput Compose(MarketAnalysisSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var marketRegime = _marketRegimeClassifier.Classify(snapshot);
        var snapshotWithConsistentRegime = WithMarketRegime(snapshot, marketRegime);
        var formattedContext = _analyticsFormatter.Format(snapshotWithConsistentRegime);

        return new AnalyticsOutput
        {
            MarketRegime = marketRegime,
            FormattedContext = formattedContext,
        };
    }

    private static MarketAnalysisSnapshot WithMarketRegime(MarketAnalysisSnapshot snapshot, string marketRegime) =>
        snapshot with
        {
            Sentiment = snapshot.Sentiment with
            {
                MarketRegime = marketRegime,
            },
        };
}
