using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Services;

/// <summary>
/// Генерирует мягкие ("soft") предупреждения для <c>snapshotHealth.warnings</c>.
/// Предупреждения не влияют на <c>isFresh</c> / <c>isPartial</c>, но сигнализируют
/// об ограничениях интерпретации данных.
///
/// Правила V1 (по приоритету при усечении):
///   — Data-quality:          near-staleness для orderBook / tradeFlow / derivatives
///   — Market-interpretation: low volume · conflicting signals · directional+neutral · far from level
///   — Context:               portfolio not included · aggregated context not included
///
/// Итоговый список урезается до <see cref="MaxWarnings"/> сообщений.
/// </summary>
internal static class SnapshotHealthWarningsBuilder
{
    /// <summary>Максимальное количество мягких предупреждений в итоговом списке.</summary>
    public const int MaxWarnings = 5;

    private const decimal LowVolumeThreshold    = 0.5m;
    private const decimal FarFromLevelThreshold = 1.5m;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Строит список мягких предупреждений для снапшота, используя указанный контекст.
    /// Результат содержит не более <see cref="MaxWarnings"/> уникальных сообщений.
    /// </summary>
    public static IReadOnlyList<string> Build(
        MarketAnalysisSnapshot snapshot,
        SnapshotHealthWarningsContext ctx)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(ctx);

        var dataQuality         = new List<string>();
        var marketInterpretation= new List<string>();
        var contextWarnings     = new List<string>();

        // Priority 1: data-quality
        AddNearStalenessWarnings(ctx, dataQuality);

        // Priority 2: market-interpretation
        AddLowVolumeWarning(snapshot, ctx.Mode, marketInterpretation);
        AddConflictingMicrostructureWarning(snapshot.Sentiment, marketInterpretation);
        AddDirectionalNeutralRegimeWarning(snapshot, ctx.Mode, marketInterpretation);
        AddFarFromLevelWarning(snapshot, ctx.Mode, marketInterpretation);

        // Priority 3: context
        AddContextWarnings(ctx, contextWarnings);

        // Merge by priority, deduplicate and truncate
        var result = new List<string>(MaxWarnings);
        foreach (var w in dataQuality.Concat(marketInterpretation).Concat(contextWarnings))
        {
            if (result.Count >= MaxWarnings) break;
            if (!result.Contains(w, StringComparer.Ordinal))
                result.Add(w);
        }

        return result;
    }

    // ─── Rule implementations ────────────────────────────────────────────────

    /// <summary>
    /// Rule 6.1: секция OrderBook / TradeFlow / Derivatives достигла порога близости к staleness,
    /// но ещё не протухла.
    /// </summary>
    internal static void AddNearStalenessWarnings(
        SnapshotHealthWarningsContext ctx,
        List<string> target)
    {
        TryAddNearStaleness("orderBook",   ctx.Thresholds.OrderBookMaxAge,   ctx, target);
        TryAddNearStaleness("tradeFlow",   ctx.Thresholds.TradeFlowMaxAge,   ctx, target);
        TryAddNearStaleness("derivatives", ctx.Thresholds.DerivativesMaxAge, ctx, target);
    }

    private static void TryAddNearStaleness(
        string sectionName,
        TimeSpan maxAge,
        SnapshotHealthWarningsContext ctx,
        List<string> target)
    {
        if (!ctx.SectionAgesMs.TryGetValue(sectionName, out var ageMs)) return;

        var maxAgeMs    = (long)maxAge.TotalMilliseconds;
        var proximityMs = (long)(maxAgeMs * ctx.StalenessProximityFactor);

        // Soft warning: возраст в зоне [proximity, maxAge). Уже устаревшие секции
        // попадают в жёсткие warnings (isFresh=false) и здесь не дублируются.
        if (ageMs >= proximityMs && ageMs < maxAgeMs)
            target.Add($"{sectionName} is near staleness threshold");
    }

    /// <summary>
    /// Rule 6.2: на любом первичном таймфрейме VolumeRatio &lt; 0.5.
    /// </summary>
    internal static void AddLowVolumeWarning(
        MarketAnalysisSnapshot snapshot,
        AnalysisMode mode,
        List<string> target)
    {
        foreach (var tf in GetPrimaryTimeframeSnapshots(snapshot, mode))
        {
            if (tf.VolumeRatio < LowVolumeThreshold)
            {
                target.Add("low volume on primary timeframes");
                return; // агрегируем в одно предупреждение
            }
        }
    }

    /// <summary>
    /// Rule 6.3: знаки OrderBookPressureScore и TradeFlowPressureScore противоположны.
    /// </summary>
    internal static void AddConflictingMicrostructureWarning(
        SentimentSnapshot sentiment,
        List<string> target)
    {
        var obScore = sentiment.OrderBookPressureScore;
        var tfScore = sentiment.TradeFlowPressureScore;

        // Конфликт: один скор явно положительный, другой явно отрицательный
        var conflicting = (obScore > 0 && tfScore < 0) || (obScore < 0 && tfScore > 0);
        if (conflicting)
            target.Add("orderBook and tradeFlow signals are conflicting");
    }

    /// <summary>
    /// Rule 6.4: хотя бы один первичный таймфрейм directional (Bullish/Bearish),
    /// но sentiment.MarketRegime == "Neutral".
    /// </summary>
    internal static void AddDirectionalNeutralRegimeWarning(
        MarketAnalysisSnapshot snapshot,
        AnalysisMode mode,
        List<string> target)
    {
        if (!string.Equals(snapshot.Sentiment.MarketRegime, "Neutral", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var tf in GetPrimaryTimeframeSnapshots(snapshot, mode))
        {
            if (tf.Trend is MarketTrend.Bullish or MarketTrend.Bearish)
            {
                target.Add("directional trend with neutral regime");
                return;
            }
        }
    }

    /// <summary>
    /// Rule 6.5: Bullish → distanceToSupport1Pct &gt; 1.5 || Bearish → distanceToResistance1Pct &gt; 1.5.
    /// </summary>
    internal static void AddFarFromLevelWarning(
        MarketAnalysisSnapshot snapshot,
        AnalysisMode mode,
        List<string> target)
    {
        foreach (var tf in GetPrimaryTimeframeSnapshots(snapshot, mode))
        {
            var isFar = tf.Trend switch
            {
                MarketTrend.Bullish => tf.DistanceToSupport1Pct > FarFromLevelThreshold,
                MarketTrend.Bearish => tf.DistanceToResistance1Pct > FarFromLevelThreshold,
                _                   => false,
            };

            if (isFar)
            {
                target.Add("price is far from nearest relevant level");
                return; // одно предупреждение на snapshot
            }
        }
    }

    /// <summary>
    /// Rule 6.6: опциональные секции контекста не запрошены.
    /// </summary>
    internal static void AddContextWarnings(
        SnapshotHealthWarningsContext ctx,
        List<string> target)
    {
        if (!ctx.IncludePortfolio)
            target.Add("portfolio context is not included");

        if (!ctx.IncludeAggregatedContext)
            target.Add("aggregated market context is not included");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает <see cref="TimeframeAnalysisSnapshot"/> для первичных таймфреймов текущего режима.
    /// </summary>
    internal static IEnumerable<TimeframeAnalysisSnapshot> GetPrimaryTimeframeSnapshots(
        MarketAnalysisSnapshot snapshot,
        AnalysisMode mode)
    {
        foreach (var label in AnalysisModeDefaults.GetPrimaryTimeframes(mode))
        {
            var tf = label switch
            {
                "15m" => snapshot.M15,
                "1h"  => snapshot.H1,
                "4h"  => snapshot.H4,
                "1d"  => snapshot.D1,
                _     => null,
            };

            if (tf is not null) yield return tf;
        }
    }
}

