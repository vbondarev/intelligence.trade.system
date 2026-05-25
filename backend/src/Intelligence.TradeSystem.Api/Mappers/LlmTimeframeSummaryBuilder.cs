using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;
// ReSharper disable once RedundantUsingDirective (needed for MarketRegimes constant)

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Централизованное построение summary-слоя для одного таймфрейма.
/// Гарантирует логическую согласованность всех полей summary между собой.
///
/// Порядок вычислений (каждый шаг использует результаты предыдущих):
/// 1. TrendStrengthLabel  — из Trend + TrendStrengthScore
/// 2. Bias                — из Trend + EMA-alignment
/// 3. IsTrendConfirmed    — из Bias + IsAboveEma200
/// 4. MomentumState       — из Bias + IsTrendConfirmed + RSI
/// 5. EntryQuality        — из Bias + IsTrendConfirmed + дистанции до уровней
/// 6. RiskFlags           — из RSI-флагов + TrendStrengthScore + VolumeRatio
///
/// Инварианты:
/// - Trend == Sideways/Unknown  →  Bias == Neutral
/// - Bias == Neutral            →  IsTrendConfirmed == false
/// - IsTrendConfirmed == true   →  Bias != Neutral
/// - Trend == Unknown           →  TrendStrengthLabel == Undefined
/// - MomentumState == "Healthy" →  IsTrendConfirmed == true &amp;&amp; Bias != Neutral
/// </summary>
internal static class LlmTimeframeSummaryBuilder
{
    /// <summary>Порог объёма: VolumeRatio ниже этого значения → <c>LowVolume</c>.</summary>
    private const decimal LowVolumeThreshold = 0.5m;

    /// <summary>
    /// Строит согласованный summary для снапшота таймфрейма.
    /// </summary>
    /// <param name="s">Снапшот таймфрейма.</param>
    /// <param name="snapshotIsFresh">
    /// <c>true</c>, если снапшот актуален (все секции моложе порогов).
    /// Передаётся из <c>LlmSnapshotHealthPayload.IsFresh</c>.
    /// По умолчанию <c>true</c> — не ограничивает quality.
    /// </param>
    /// <param name="marketRegime">
    /// Текущий рыночный режим (<see cref="MarketRegimes"/>).
    /// Передаётся из <c>SentimentSnapshot.MarketRegime</c>.
    /// По умолчанию <see cref="MarketRegimes.Trending"/> — не ограничивает quality.
    /// </param>
    /// <param name="higherTfOppositeLevel">
    /// Ближайший противоположный уровень со старшего таймфрейма.
    /// Для Bullish — resistance; для Bearish — support.
    /// <c>null</c> — нет уровня на старших ТФ; качество не ограничивается этим источником.
    /// </param>
    public static LlmTimeframeSummaryResult Build(
        TimeframeAnalysisSnapshot s,
        bool snapshotIsFresh = true,
        string marketRegime = MarketRegimes.Trending,
        NearestOppositeLevel? higherTfOppositeLevel = null)
    {
        var trendStrengthLabel = ComputeTrendStrengthLabel(s.Trend, s.TrendStrengthScore);
        var bias = ComputeBias(s.Trend, s.EmaBullishAlignment, s.EmaBearishAlignment);
        var isTrendConfirmed = ComputeIsTrendConfirmed(s.Trend, s.EmaBullishAlignment, s.EmaBearishAlignment, s.IsAboveEma200);
        var momentumState = ComputeMomentumState(bias, isTrendConfirmed, s.Rsi14, s.RsiOverbought, s.RsiOversold, s.Rsi14IsReliable);

        // Pre-compute entry level and nearest opposite level for quality + risk flags
        var entryLevelStrength = ResolveEntryLevelStrength(bias, s);
        var (oppDistancePct, oppStrength, isOppFromHigherTf) = ResolveNearestOppositeLevel(bias, s, higherTfOppositeLevel);

        var entryQuality = ComputeEntryQuality(bias, isTrendConfirmed, s, snapshotIsFresh, marketRegime,
            entryLevelStrength, oppDistancePct, oppStrength);
        var riskFlags = ComputeRiskFlags(s, bias, entryLevelStrength, oppDistancePct, isOppFromHigherTf);

        return new LlmTimeframeSummaryResult
        {
            TrendStrengthLabel = trendStrengthLabel,
            Bias = bias,
            IsTrendConfirmed = isTrendConfirmed,
            MomentumState = momentumState,
            EntryQuality = entryQuality,
            RiskFlags = riskFlags,
        };
    }

    // ─── Step 1: TrendStrengthLabel ──────────────────────────────────────────

    private static TrendStrengthLabel ComputeTrendStrengthLabel(MarketTrend trend, decimal score) =>
        TrendStrengthLabelMapper.Map(trend, score);

    // ─── Step 2: Bias ────────────────────────────────────────────────────────

    /// <summary>
    /// Bullish: trend == Bullish &amp;&amp; emaBullish.<br/>
    /// Bearish: trend == Bearish &amp;&amp; emaBearish.<br/>
    /// Neutral: Sideways / Unknown / EMA-конфликт (trend и alignment не совпадают).
    /// </summary>
    private static TimeframeBias ComputeBias(MarketTrend trend, bool emaBullish, bool emaBearish)
    {
        if (trend == MarketTrend.Unknown || trend == MarketTrend.Sideways)
            return TimeframeBias.Neutral;

        if (trend == MarketTrend.Bullish && emaBullish) return TimeframeBias.Bullish;
        if (trend == MarketTrend.Bearish && emaBearish) return TimeframeBias.Bearish;

        return TimeframeBias.Neutral; // EMA conflict
    }

    // ─── Step 3: IsTrendConfirmed ────────────────────────────────────────────

    /// <summary>
    /// Bullish: emaBullishAlignment &amp;&amp; isAboveEma200.<br/>
    /// Bearish: emaBearishAlignment &amp;&amp; !isAboveEma200.<br/>
    /// Sideways / Unknown: всегда false.
    /// </summary>
    private static bool ComputeIsTrendConfirmed(
        MarketTrend trend, bool emaBullish, bool emaBearish, bool isAboveEma200) =>
        trend switch
        {
            MarketTrend.Bullish => emaBullish && isAboveEma200,
            MarketTrend.Bearish => emaBearish && !isAboveEma200,
            _ => false,
        };

    // ─── Step 4: MomentumState ───────────────────────────────────────────────

    /// <summary>
    /// Bullish: Overextended → Healthy → Weak (по приоритету RSI)<br/>
    /// Bearish: Overextended → Healthy → Weak<br/>
    /// Neutral bias: всегда Neutral.
    /// </summary>
    private static MomentumState ComputeMomentumState(
        TimeframeBias bias, bool isTrendConfirmed, decimal? rsi14,
        bool rsiOverbought, bool rsiOversold, bool rsi14IsReliable) =>
        bias switch
        {
            TimeframeBias.Bullish when rsi14IsReliable && (rsiOverbought || rsi14 > 70m) => MomentumState.Overextended,
            TimeframeBias.Bullish when rsi14IsReliable && isTrendConfirmed && rsi14 >= 55m && rsi14 <= 70m => MomentumState.Healthy,
            TimeframeBias.Bullish => MomentumState.Weak,

            TimeframeBias.Bearish when rsi14IsReliable && (rsiOversold || rsi14 < 30m) => MomentumState.Overextended,
            TimeframeBias.Bearish when rsi14IsReliable && isTrendConfirmed && rsi14 >= 30m && rsi14 <= 45m => MomentumState.Healthy,
            TimeframeBias.Bearish => MomentumState.Weak,

            _ => MomentumState.Neutral,
        };

    // ─── Step 5: EntryQuality ────────────────────────────────────────────────

    private static EntryQuality ComputeEntryQuality(
        TimeframeBias bias, bool isTrendConfirmed, TimeframeAnalysisSnapshot s,
        bool snapshotIsFresh, string marketRegime,
        decimal? entryLevelStrength, decimal? oppDistancePct, decimal? oppStrength)
    {
        var raw = EntryQualityEvaluator.Evaluate(
            bias, isTrendConfirmed,
            s.Support1, s.DistanceToSupport1Pct, s.RsiOverbought,
            s.Resistance1, s.DistanceToResistance1Pct, s.RsiOversold,
            s.VolumeRatio, s.IsAboveEma20, s.IsAboveEma50,
            snapshotIsFresh, marketRegime,
            entryLevelStrength, oppDistancePct, oppStrength);

        return ApplyIndicatorCap(s, raw);
    }

    /// <summary>
    /// Ограничивает качество точки входа исходя из доступности ключевых индикаторов.<br/>
    /// Правило: unavailable → не выше Poor; fallback → не выше Fair.
    /// </summary>
    private static EntryQuality ApplyIndicatorCap(TimeframeAnalysisSnapshot s, EntryQuality computed)
    {
        // Если любой ключевой индикатор недоступен — cap на Poor.
        if (!s.Rsi14IsReliable || !s.AtrIsReliable || !s.EmaIsReliable)
            return EntryQuality.Poor;

        // Если ATR или EMA рассчитаны по fallback — cap на Fair.
        // VolumeRatioIsFallback не ограничивает entryQuality: объём — вспомогательный сигнал.
        if (s.AtrIsFallback || s.EmaHasFallback)
            return (EntryQuality)Math.Max((int)computed, (int)EntryQuality.Fair);

        return computed;
    }

    // ─── Step 6: RiskFlags ───────────────────────────────────────────────────

    private static List<string> ComputeRiskFlags(
        TimeframeAnalysisSnapshot s,
        TimeframeBias bias,
        decimal? entryLevelStrength,
        decimal? oppDistancePct,
        bool isOppFromHigherTf)
    {
        var flags = new List<string>();

        // ── RSI ──────────────────────────────────────────────────────────────
        if (!s.Rsi14IsReliable)
        {
            flags.Add("RsiUnavailable");
        }
        else
        {
            if (s.RsiOverbought) flags.Add("RsiOverbought");
            if (s.RsiOversold) flags.Add("RsiOversold");
        }

        // ── ATR ──────────────────────────────────────────────────────────────
        if (!s.AtrIsReliable)
            flags.Add("AtrUnavailable");
        else if (s.AtrIsFallback)
            flags.Add("AtrFallback");

        // ── Volume ───────────────────────────────────────────────────────────
        if (!s.VolumeRatioIsReliable)
            flags.Add("VolumeDataUnavailable");
        else if (s.VolumeRatioIsFallback)
        {
            flags.Add("VolumeDataFallback");
            // Fallback-значение можно использовать, поэтому LowVolume тоже эмитируем.
            if (s.VolumeRatio.GetValueOrDefault() < LowVolumeThreshold)
                flags.Add("LowVolume");
        }
        else if (s.VolumeRatio.GetValueOrDefault() < LowVolumeThreshold)
            flags.Add("LowVolume");

        // ── Opposite level proximity ─────────────────────────────────────────
        if (oppDistancePct < EntryQualityEvaluator.NearOppositeThreshold)
        {
            if (bias == TimeframeBias.Bullish)
                flags.Add(isOppFromHigherTf ? "NearHigherTimeframeResistance" : "NearResistance");
            else if (bias == TimeframeBias.Bearish)
                flags.Add(isOppFromHigherTf ? "NearHigherTimeframeSupport" : "NearSupport");
        }

        // ── Weak entry level ─────────────────────────────────────────────────
        if (bias != TimeframeBias.Neutral &&
            EntryQualityEvaluator.ClassifyStrength(entryLevelStrength)
                is LevelStrengthCategory.Weak or LevelStrengthCategory.Unknown)
            flags.Add("WeakEntryLevel");

        // ── General IndicatorUnavailable ─────────────────────────────────────
        var anyUnavailable = !s.EmaIsReliable || !s.Rsi14IsReliable || !s.AtrIsReliable || !s.VolumeRatioIsReliable;
        if (anyUnavailable && !flags.Contains("IndicatorUnavailable"))
            flags.Add("IndicatorUnavailable");

        // ── General IndicatorFallback ───────────────────────────────���────────
        var anyFallback = s.EmaHasFallback || s.AtrIsFallback || s.VolumeRatioIsFallback;
        if (anyFallback && !flags.Contains("IndicatorFallback"))
            flags.Add("IndicatorFallback");

        // ── Weak trend ───────────────────────────────────────────────────────
        if (s.TrendStrengthScore < TrendStrengthLabelMapper.ModerateThreshold)
            flags.Add("WeakTrend");

        return flags;
    }

    // ─── Entry level / opposite level resolution ─────────────────────────────

    /// <summary>
    /// Возвращает нормализованную силу уровня входа для данного bias.
    /// Bullish → сила Support1; Bearish → сила Resistance1; Neutral → null.
    /// </summary>
    private static decimal? ResolveEntryLevelStrength(TimeframeBias bias, TimeframeAnalysisSnapshot s) =>
        bias switch
        {
            TimeframeBias.Bullish => s.Support1Strength,
            TimeframeBias.Bearish => s.Resistance1Strength,
            _ => null,
        };

    /// <summary>
    /// Возвращает ближайший противоположный уровень (текущий ТФ vs старший ТФ).
    /// Для Bullish — ближайший resistance; для Bearish — ближайший support.
    /// </summary>
    private static (decimal? dist, decimal? strength, bool isHigherTf) ResolveNearestOppositeLevel(
        TimeframeBias bias, TimeframeAnalysisSnapshot s, NearestOppositeLevel? higherTf)
    {
        // Current TF opposite level
        var (currentDist, currentStrength) = bias switch
        {
            TimeframeBias.Bullish => (s.DistanceToResistance1Pct, s.Resistance1Strength),
            TimeframeBias.Bearish => (s.DistanceToSupport1Pct, s.Support1Strength),
            _ => (null, null),
        };

        if (currentDist is null && higherTf is null) return (null, null, false);
        if (currentDist is null) return (higherTf!.DistancePct, higherTf.Strength, true);
        if (higherTf is null) return (currentDist, currentStrength, false);

        // Both available: choose closer
        return currentDist <= higherTf.DistancePct
            ? (currentDist, currentStrength, false)
            : (higherTf.DistancePct, higherTf.Strength, true);
    }
}
