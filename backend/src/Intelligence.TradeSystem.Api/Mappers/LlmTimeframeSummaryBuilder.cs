using Intelligence.TradeSystem.Api.Models.Payloads;

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
/// 6. RiskFlags           — синхронизированы с причинами понижения EntryQuality
///
/// Инварианты:
/// - Trend == Sideways/Unknown  →  Bias == Neutral
/// - Bias == Neutral            →  IsTrendConfirmed == false
/// - IsTrendConfirmed == true   →  Bias != Neutral
/// - Trend == Unknown           →  TrendStrengthLabel == Undefined
/// - MomentumState == Healthy   →  IsTrendConfirmed == true &amp;&amp; Bias != Neutral
/// </summary>
internal static class LlmTimeframeSummaryBuilder
{
    /// <summary>Порог объёма: VolumeRatio ниже этого значения → LowVolume.</summary>
    private const decimal LowVolumeThreshold = 0.50m;

    /// <summary>Порог объёма: VolumeRatio ниже этого значения → VeryLowVolume.</summary>
    private const decimal VeryLowVolumeThreshold = 0.25m;

    /// <summary>
    /// Порог для определения, что цена находится между двумя значимыми уровнями.
    /// NearResistance/NearSupport остаются более строгими и используют EntryQualityEvaluator.NearOppositeThreshold.
    /// </summary>
    private const decimal RangeLevelDistanceThreshold = 0.75m;

    /// <summary>
    /// Строит согласованный summary для снапшота таймфрейма.
    /// </summary>
    /// <param name="s">Снапшот таймфрейма.</param>
    /// <param name="snapshotIsFresh">
    /// true, если снапшот актуален. Должен передаваться из LlmSnapshotHealthPayload.IsFresh.
    /// </param>
    /// <param name="marketRegime">
    /// Текущий рыночный режим. Должен передаваться из SentimentSnapshot.MarketRegime.
    /// </param>
    /// <param name="higherTfOppositeLevel">
    /// Ближайший противоположный уровень со старшего таймфрейма.
    /// Для Bullish — resistance; для Bearish — support.
    /// null — нет уровня на старших ТФ; качество не ограничивается этим источником.
    /// </param>
    public static LlmTimeframeSummaryResult Build(
        TimeframeAnalysisSnapshot s,
        bool snapshotIsFresh,
        string? marketRegime,
        NearestOppositeLevel? higherTfOppositeLevel = null)
    {
        var normalizedMarketRegime = NormalizeMarketRegime(marketRegime);

        var trendStrengthLabel = ComputeTrendStrengthLabel(s.Trend, s.TrendStrengthScore);
        var bias = ComputeBias(s.Trend, s.EmaBullishAlignment, s.EmaBearishAlignment);
        var isTrendConfirmed = ComputeIsTrendConfirmed(s.Trend, s.EmaBullishAlignment, s.EmaBearishAlignment, s.IsAboveEma200);
        var momentumState = ComputeMomentumState(
            bias,
            isTrendConfirmed,
            s.Rsi14,
            s.RsiOverbought,
            s.RsiOversold,
            s.Rsi14IsReliable);

        var entryLevelStrength = ResolveEntryLevelStrength(bias, s);
        var (oppDistancePct, oppStrength, isOppFromHigherTf) = ResolveNearestOppositeLevel(
            bias,
            s,
            higherTfOppositeLevel);

        var entryQuality = ComputeEntryQuality(
            bias,
            isTrendConfirmed,
            s,
            snapshotIsFresh,
            normalizedMarketRegime,
            entryLevelStrength,
            oppDistancePct,
            oppStrength);

        var riskFlags = ComputeRiskFlags(
            s,
            bias,
            isTrendConfirmed,
            entryQuality,
            momentumState,
            snapshotIsFresh,
            normalizedMarketRegime,
            entryLevelStrength,
            oppDistancePct,
            isOppFromHigherTf);

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
    /// Bullish: trend == Bullish &amp;&amp; emaBullish.
    /// Bearish: trend == Bearish &amp;&amp; emaBearish.
    /// Neutral: Sideways / Unknown / EMA-конфликт.
    /// </summary>
    private static TimeframeBias ComputeBias(MarketTrend trend, bool emaBullish, bool emaBearish)
    {
        if (trend == MarketTrend.Unknown || trend == MarketTrend.Sideways)
            return TimeframeBias.Neutral;

        if (trend == MarketTrend.Bullish && emaBullish)
            return TimeframeBias.Bullish;

        if (trend == MarketTrend.Bearish && emaBearish)
            return TimeframeBias.Bearish;

        return TimeframeBias.Neutral;
    }

    // ─── Step 3: IsTrendConfirmed ────────────────────────────────────────────

    /// <summary>
    /// Structural confirmation:
    /// Bullish: emaBullishAlignment &amp;&amp; price above EMA200.
    /// Bearish: emaBearishAlignment &amp;&amp; price below EMA200.
    /// Execution filters are applied later by EntryQuality/RiskFlags.
    /// </summary>
    private static bool ComputeIsTrendConfirmed(
        MarketTrend trend,
        bool emaBullish,
        bool emaBearish,
        bool isAboveEma200) =>
        trend switch
        {
            MarketTrend.Bullish => emaBullish && isAboveEma200,
            MarketTrend.Bearish => emaBearish && !isAboveEma200,
            _ => false,
        };

    // ─── Step 4: MomentumState ───────────────────────────────────────────────

    private static MomentumState ComputeMomentumState(
        TimeframeBias bias,
        bool isTrendConfirmed,
        decimal? rsi14,
        bool rsiOverbought,
        bool rsiOversold,
        bool rsi14IsReliable) =>
        bias switch
        {
            TimeframeBias.Bullish when rsi14IsReliable && (rsiOverbought || rsi14 > 70m)
                => MomentumState.Overextended,

            TimeframeBias.Bullish when rsi14IsReliable && isTrendConfirmed && rsi14 >= 55m && rsi14 <= 70m
                => MomentumState.Healthy,

            TimeframeBias.Bullish
                => MomentumState.Weak,

            TimeframeBias.Bearish when rsi14IsReliable && (rsiOversold || rsi14 < 30m)
                => MomentumState.Overextended,

            TimeframeBias.Bearish when rsi14IsReliable && isTrendConfirmed && rsi14 >= 30m && rsi14 <= 45m
                => MomentumState.Healthy,

            TimeframeBias.Bearish
                => MomentumState.Weak,

            _ => MomentumState.Neutral,
        };

    // ─── Step 5: EntryQuality ────────────────────────────────────────────────

    private static EntryQuality ComputeEntryQuality(
        TimeframeBias bias,
        bool isTrendConfirmed,
        TimeframeAnalysisSnapshot s,
        bool snapshotIsFresh,
        string? marketRegime,
        decimal? entryLevelStrength,
        decimal? oppDistancePct,
        decimal? oppStrength)
    {
        var raw = EntryQualityEvaluator.Evaluate(
            bias,
            isTrendConfirmed,
            s.Support1,
            s.DistanceToSupport1Pct,
            s.RsiOverbought,
            s.Resistance1,
            s.DistanceToResistance1Pct,
            s.RsiOversold,
            s.VolumeRatio,
            s.IsAboveEma20,
            s.IsAboveEma50,
            snapshotIsFresh,
            marketRegime,
            entryLevelStrength,
            oppDistancePct,
            oppStrength);

        return ApplyIndicatorCap(s, raw);
    }

    /// <summary>
    /// Ограничивает качество точки входа исходя из доступности ключевых индикаторов.
    /// unavailable → Poor; fallback → не выше Fair.
    /// </summary>
    private static EntryQuality ApplyIndicatorCap(TimeframeAnalysisSnapshot s, EntryQuality computed)
    {
        if (!s.Rsi14IsReliable || !s.AtrIsReliable || !s.EmaIsReliable)
            return EntryQuality.Poor;

        if (s.AtrIsFallback || s.EmaHasFallback)
            return CapAt(computed, EntryQuality.Fair);

        return computed;
    }

    private static EntryQuality CapAt(EntryQuality quality, EntryQuality maxQuality) =>
        quality < maxQuality ? maxQuality : quality;

    // ─── Step 6: RiskFlags ───────────────────────────────────────────────────

    private static List<string> ComputeRiskFlags(
        TimeframeAnalysisSnapshot s,
        TimeframeBias bias,
        bool isTrendConfirmed,
        EntryQuality entryQuality,
        MomentumState momentumState,
        bool snapshotIsFresh,
        string? marketRegime,
        decimal? entryLevelStrength,
        decimal? oppDistancePct,
        bool isOppFromHigherTf)
    {
        var flags = new List<string>();

        void Add(string flag)
        {
            if (!flags.Contains(flag, StringComparer.Ordinal))
                flags.Add(flag);
        }

        var isNeutralMarketRegime = IsNeutralMarketRegime(marketRegime);

        // ── Stale snapshot ───────────────────────────────────────────────────
        if (!snapshotIsFresh)
            Add("StaleSnapshot");

        // ── RSI ───────────────────────────────────────────────────────────────
        if (!s.Rsi14IsReliable)
        {
            Add("RsiUnavailable");
        }
        else
        {
            if (s.RsiOverbought)
                Add("RsiOverbought");

            if (s.RsiOversold)
                Add("RsiOversold");

            if (bias == TimeframeBias.Bullish && s.Rsi14 < 50m)
                Add("RsiAgainstBullishBias");
            else if (bias == TimeframeBias.Bearish && s.Rsi14 > 50m)
                Add("RsiAgainstBearishBias");
        }

        // ── Momentum ─────────────────────────────────────────────────────────
        if (bias != TimeframeBias.Neutral && momentumState == MomentumState.Weak)
            Add("WeakMomentum");

        // ── ATR ──────────────────────────────────────────────────────────────
        if (!s.AtrIsReliable)
            Add("AtrUnavailable");
        else if (s.AtrIsFallback)
            Add("AtrFallback");

        // ── Volume ───────────────────────────────────────────────────────────
        if (!s.VolumeRatioIsReliable)
        {
            Add("VolumeDataUnavailable");
        }
        else
        {
            if (s.VolumeRatioIsFallback)
                Add("VolumeDataFallback");

            AddVolumeThresholdFlags(s.VolumeRatio, Add);
        }

        // ── EMA conflict flags ───────────────────────────────────────────────
        if (!s.EmaIsReliable)
        {
            Add("EmaDataUnavailable");
        }
        else
        {
            AddEmaRiskFlags(s, bias, Add);
        }

        // ── Opposite level proximity ─────────────────────────────────────────
        if (oppDistancePct is >= 0m and < EntryQualityEvaluator.NearOppositeThreshold)
        {
            if (bias == TimeframeBias.Bullish)
                Add(isOppFromHigherTf ? "NearHigherTimeframeResistance" : "NearResistance");
            else if (bias == TimeframeBias.Bearish)
                Add(isOppFromHigherTf ? "NearHigherTimeframeSupport" : "NearSupport");
        }

        // For Neutral bias: check both sides independently.
        if (bias == TimeframeBias.Neutral)
        {
            if (s.DistanceToResistance1Pct is >= 0m and < EntryQualityEvaluator.NearOppositeThreshold)
                Add("NearResistance");

            if (s.DistanceToSupport1Pct is >= 0m and < EntryQualityEvaluator.NearOppositeThreshold)
                Add("NearSupport");
        }

        // ── Entry level: missing or weak ─────────────────────────────────────
        AddEntryLevelRiskFlags(s, bias, entryLevelStrength, Add);

        // ── Market regime ────────────────────────────────────────────────────
        if (bias != TimeframeBias.Neutral && isNeutralMarketRegime)
        {
            Add("NeutralMarketRegime");

            if (isTrendConfirmed)
                Add("DirectionalTrendWithNeutralRegime");
        }

        // ── Trend confirmed but entry filtered ───────────────────────────────
        if (isTrendConfirmed && entryQuality != EntryQuality.Good)
            Add("TrendConfirmedButEntryFiltered");

        // ── Range / structure ────────────────────────────────────────────────
        if (bias == TimeframeBias.Neutral)
        {
            Add("NeutralBias");
            Add("RangeBound");

            if (IsBetweenRelevantSupportAndResistance(s))
                Add("BetweenStrongSupportAndResistance");
        }

        // ── General IndicatorUnavailable / IndicatorFallback ─────────────────
        if (!s.EmaIsReliable || !s.Rsi14IsReliable || !s.AtrIsReliable || !s.VolumeRatioIsReliable)
            Add("IndicatorUnavailable");

        if (s.EmaHasFallback || s.AtrIsFallback || s.VolumeRatioIsFallback)
            Add("IndicatorFallback");

        // ── Weak trend ───────────────────────────────────────────────────────
        if (s.TrendStrengthScore < TrendStrengthLabelMapper.ModerateThreshold)
            Add("WeakTrend");

        return flags;
    }

    private static void AddEmaRiskFlags(
        TimeframeAnalysisSnapshot s,
        TimeframeBias bias,
        Action<string> add)
    {
        if (bias == TimeframeBias.Bullish)
        {
            if (!s.IsAboveEma20)
                add("BelowEma20");

            if (!s.IsAboveEma50)
                add("BelowEma50");

            if (!s.IsAboveEma20 || !s.IsAboveEma50)
                add("EmaConflict");

            return;
        }

        if (bias == TimeframeBias.Bearish)
        {
            if (s.IsAboveEma20)
                add("AboveEma20");

            if (s.IsAboveEma50)
                add("AboveEma50");

            if (s.IsAboveEma20 || s.IsAboveEma50)
                add("EmaConflict");

            return;
        }

        // Neutral bias: do not add directional EMA flags, but mark mixed/structural conflict.
        if (s.Trend == MarketTrend.Bullish || s.Trend == MarketTrend.Bearish)
            add("EmaConflict");

        if (s.IsAboveEma20 != s.IsAboveEma50)
            add("MixedEmaState");
    }

    private static void AddEntryLevelRiskFlags(
        TimeframeAnalysisSnapshot s,
        TimeframeBias bias,
        decimal? entryLevelStrength,
        Action<string> add)
    {
        if (bias == TimeframeBias.Bullish)
        {
            if (s.Support1 is null)
            {
                add("MissingEntryLevel");
                return;
            }

            if (EntryQualityEvaluator.ClassifyStrength(entryLevelStrength)
                is LevelStrengthCategory.Weak or LevelStrengthCategory.Unknown)
            {
                add("WeakEntryLevel");
            }

            return;
        }

        if (bias == TimeframeBias.Bearish)
        {
            if (s.Resistance1 is null)
            {
                add("MissingEntryLevel");
                return;
            }

            if (EntryQualityEvaluator.ClassifyStrength(entryLevelStrength)
                is LevelStrengthCategory.Weak or LevelStrengthCategory.Unknown)
            {
                add("WeakEntryLevel");
            }
        }
    }

    /// <summary>
    /// Добавляет LowVolume и, при необходимости, уточняющий VeryLowVolume.
    /// VeryLowVolume не заменяет LowVolume, а дополняет его.
    /// </summary>
    private static void AddVolumeThresholdFlags(decimal? volumeRatio, Action<string> add)
    {
        if (volumeRatio is null)
        {
            add("VolumeDataUnavailable");
            add("LowVolume");
            return;
        }

        if (volumeRatio < LowVolumeThreshold)
            add("LowVolume");

        if (volumeRatio < VeryLowVolumeThreshold)
            add("VeryLowVolume");
    }

    private static bool IsBetweenRelevantSupportAndResistance(TimeframeAnalysisSnapshot s) =>
        s.Support1Strength >= LevelStrengthLabelMapper.ModerateThreshold
        && s.Resistance1Strength >= LevelStrengthLabelMapper.ModerateThreshold
        && s.DistanceToSupport1Pct is >= 0m and < RangeLevelDistanceThreshold
        && s.DistanceToResistance1Pct is >= 0m and < RangeLevelDistanceThreshold;

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
    /// Возвращает ближайший противоположный уровень.
    /// Для Bullish — ближайший resistance.
    /// Для Bearish — ближайший support.
    /// Отрицательная дистанция означает wrong-side-of-price и считается отсутствующей.
    /// </summary>
    private static (decimal? dist, decimal? strength, bool isHigherTf) ResolveNearestOppositeLevel(
        TimeframeBias bias,
        TimeframeAnalysisSnapshot s,
        NearestOppositeLevel? higherTf)
    {
        var (currentRawDist, currentRawStrength) = bias switch
        {
            TimeframeBias.Bullish => (s.DistanceToResistance1Pct, s.Resistance1Strength),
            TimeframeBias.Bearish => (s.DistanceToSupport1Pct, s.Support1Strength),
            _ => ((decimal?)null, (decimal?)null),
        };

        var current = NormalizeOppositeLevelCandidate(currentRawDist, currentRawStrength, isHigherTf: false);

        var higher = higherTf is null
            ? (dist: (decimal?)null, strength: (decimal?)null, isHigherTf: false)
            : NormalizeOppositeLevelCandidate(higherTf.DistancePct, higherTf.Strength, isHigherTf: true);

        if (current.dist is null && higher.dist is null)
            return (null, null, false);

        if (current.dist is null)
            return higher;

        if (higher.dist is null)
            return current;

        return current.dist <= higher.dist
            ? current
            : higher;
    }

    /// <summary>
    /// Нормализует кандидата: отрицательная дистанция (уровень на неправильной стороне цены)
    /// возвращается как (null, null, false). Нулевая дистанция валидна.
    /// </summary>
    private static (decimal? dist, decimal? strength, bool isHigherTf) NormalizeOppositeLevelCandidate(
        decimal? distancePct,
        decimal? strength,
        bool isHigherTf)
    {
        if (distancePct is null || distancePct < 0m)
            return (null, null, false);

        return (distancePct, strength, isHigherTf);
    }

    // ─── Market regime helpers ───────────────────────────────────────────────

    private static string? NormalizeMarketRegime(string? marketRegime) =>
        string.IsNullOrWhiteSpace(marketRegime) ? null : marketRegime.Trim();

    private static bool IsNeutralMarketRegime(string? marketRegime) =>
        string.Equals(
            marketRegime?.Trim(),
            MarketRegimes.Neutral,
            StringComparison.OrdinalIgnoreCase);
}
