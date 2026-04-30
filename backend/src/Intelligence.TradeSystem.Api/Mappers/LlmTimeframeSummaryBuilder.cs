using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

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
    private const decimal TrendStrengthStrongThreshold   = 0.80m;
    private const decimal TrendStrengthModerateThreshold = 0.50m;

    /// <summary>
    /// Строит согласованный summary для снапшота таймфрейма.
    /// </summary>
    public static LlmTimeframeSummaryResult Build(TimeframeAnalysisSnapshot s)
    {
        var trendStrengthLabel = ComputeTrendStrengthLabel(s.Trend, s.TrendStrengthScore);
        var bias               = ComputeBias(s.Trend, s.EmaBullishAlignment, s.EmaBearishAlignment);
        var isTrendConfirmed   = ComputeIsTrendConfirmed(s.Trend, s.EmaBullishAlignment, s.EmaBearishAlignment, s.IsAboveEma200);
        var momentumState      = ComputeMomentumState(bias, isTrendConfirmed, s.Rsi14, s.RsiOverbought, s.RsiOversold);
        var entryQuality       = ComputeEntryQuality(bias, isTrendConfirmed, s);
        var riskFlags          = ComputeRiskFlags(s);

        return new LlmTimeframeSummaryResult
        {
            TrendStrengthLabel = trendStrengthLabel,
            Bias               = bias,
            IsTrendConfirmed   = isTrendConfirmed,
            MomentumState      = momentumState,
            EntryQuality       = entryQuality,
            RiskFlags          = riskFlags,
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
            _                   => false,
        };

    // ─── Step 4: MomentumState ───────────────────────────────────────────────

    /// <summary>
    /// Bullish: Overextended → Healthy → Weak (по приоритету RSI)<br/>
    /// Bearish: Overextended → Healthy → Weak<br/>
    /// Neutral bias: всегда Neutral.
    /// </summary>
    private static MomentumState ComputeMomentumState(
        TimeframeBias bias, bool isTrendConfirmed, decimal rsi14, bool rsiOverbought, bool rsiOversold) =>
        bias switch
        {
            TimeframeBias.Bullish when rsiOverbought || rsi14 > 70m                     => MomentumState.Overextended,
            TimeframeBias.Bullish when isTrendConfirmed && rsi14 >= 55m && rsi14 <= 70m => MomentumState.Healthy,
            TimeframeBias.Bullish                                                        => MomentumState.Weak,

            TimeframeBias.Bearish when rsiOversold || rsi14 < 30m                       => MomentumState.Overextended,
            TimeframeBias.Bearish when isTrendConfirmed && rsi14 >= 30m && rsi14 <= 45m => MomentumState.Healthy,
            TimeframeBias.Bearish                                                        => MomentumState.Weak,

            _ => MomentumState.Neutral,
        };

    // ─── Step 5: EntryQuality ────────────────────────────────────────────────

    private static EntryQuality ComputeEntryQuality(
        TimeframeBias bias, bool isTrendConfirmed, TimeframeAnalysisSnapshot s) =>
        EntryQualityEvaluator.Evaluate(
            bias, isTrendConfirmed,
            s.Support1,    s.DistanceToSupport1Pct,    s.RsiOverbought,
            s.Resistance1, s.DistanceToResistance1Pct, s.RsiOversold);

    // ─── Step 6: RiskFlags ───────────────────────────────────────────────────

    private static List<string> ComputeRiskFlags(TimeframeAnalysisSnapshot s)
    {
        var flags = new List<string>();
        if (s.RsiOverbought)                                        flags.Add("RsiOverbought");
        if (s.RsiOversold)                                          flags.Add("RsiOversold");
        if (s.TrendStrengthScore < TrendStrengthLabelMapper.ModerateThreshold) flags.Add("WeakTrend");
        if (s.VolumeRatio < 1.0m)                                   flags.Add("LowVolume");
        return flags;
    }
}







