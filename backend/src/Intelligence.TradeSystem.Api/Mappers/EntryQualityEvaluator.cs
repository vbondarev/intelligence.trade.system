using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Централизованное вычисление <c>summary.entryQuality</c> V1.
///
/// Whitelist допустимых значений:
/// <list type="bullet">
///   <item><term>Good</term>  <description>Подтверждённый bias, цена близко к релевантному уровню, RSI не экстремальный, объём нормальный, EMA подтверждают направление, снапшот свежий, режим не Neutral.</description></item>
///   <item><term>Fair</term>  <description>Bias присутствует, умеренная дистанция до уровня, RSI не экстремальный. Допустим при небольших ограничениях.</description></item>
///   <item><term>Poor</term>  <description>Нейтральный bias, отсутствие уровня, дальняя дистанция, RSI экстремальный, низкий объём, EMA-конфликт, устаревший снапшот или neutral режим без подтверждений.</description></item>
/// </list>
///
/// Детерминированная формула V1 (baseQuality + downgrade rules):
/// <code>
/// Neutral  → Poor
/// Bullish/Bearish:
///   1. baseQuality = EvaluateLevelBasedQuality(...)
///   2. ApplyVolumeRule:        volumeRatio &lt; 0.25 → Poor; &lt; 0.5 �� cap Fair; null → cap Fair
///   3. ApplyEmaRule:           EMA-конфликт с bias → Poor (оба) / cap Fair (один)
///   4. ApplySnapshotFreshnessRule: !fresh → cap Fair; !fresh &amp;&amp; lowVolume → Poor
///   5. ApplyMarketRegimeRule:  Neutral + (lowVolume || EMA-конфликт) → Poor; Neutral → cap Fair
/// </code>
///
/// Инварианты:
/// - bias == Neutral                    →  entryQuality == Poor
/// - support1 == null (bullish)         →  entryQuality == Poor
/// - resistance1 == null (bearish)      →  entryQuality == Poor
/// - rsiOverbought (bullish)            →  entryQuality == Poor
/// - rsiOversold (bearish)              →  entryQuality == Poor
/// - entryQuality == Good               →  isTrendConfirmed == true &amp;&amp; dist &lt;= GoodMaxDistance
/// - entryQuality == Good               →  volumeRatio &gt;= 0.5 (или недоступен → не Good)
/// - entryQuality == Good               →  EMA подтверждают bias
/// - entryQuality == Good               →  snapshotIsFresh == true
/// - entryQuality == Good               →  marketRegime != Neutral (или сильные подтверждения)
/// </summary>
internal static class EntryQualityEvaluator
{
    /// <summary>Максимальная дистанция до уровня для оценки <c>Good</c>.</summary>
    internal const decimal GoodMaxDistance = 0.75m;

    /// <summary>Максимальная дистанция до уровня для оценки <c>Fair</c>.</summary>
    internal const decimal FairMaxDistance = 1.50m;

    /// <summary>Порог очень низкого объёма: ниже этого значения → <c>Poor</c>.</summary>
    internal const decimal VeryLowVolumeThreshold = 0.25m;

    /// <summary>Порог низкого объёма: ниже этого значения → не выше <c>Fair</c>.</summary>
    internal const decimal LowVolumeThreshold = 0.50m;

    /// <summary>
    /// Порог «очень близкого» противоположного уровня.
    /// Если уровень ближе этого значения и сила Moderate/Strong → <c>Poor</c>.
    /// </summary>
    internal const decimal CloseOppositeThreshold = 0.15m;

    /// <summary>
    /// Порог «близкого» противоположного уровня.
    /// Если уровень ближе этого значения → <c>Good</c> запрещён (cap <c>Fair</c>).
    /// Если ещё и сила Moderate/Strong → cap <c>Fair</c>.
    /// </summary>
    internal const decimal NearOppositeThreshold = 0.30m;

    /// <summary>Максимальная нормализованная сила слабого уровня (Weak).</summary>
    internal const decimal WeakStrengthMax = 0.35m;

    /// <summary>Максимальная нормализованная сила умеренного уровня (Moderate).</summary>
    internal const decimal ModerateStrengthMax = 0.70m;

    /// <summary>
    /// Вычисляет качество точки входа на основе bias, подтверждения тренда,
    /// близости к релевантному уровню, состояния RSI и дополнительных фильтров
    /// (объём, EMA, свежесть снапшота, рыночный режим).
    /// </summary>
    public static EntryQuality Evaluate(
        TimeframeBias bias,
        bool isTrendConfirmed,
        decimal? support1,
        decimal? distanceToSupport1Pct,
        bool rsiOverbought,
        decimal? resistance1,
        decimal? distanceToResistance1Pct,
        bool rsiOversold,
        decimal? volumeRatio,
        bool isAboveEma20,
        bool isAboveEma50,
        bool snapshotIsFresh,
        string marketRegime,
        decimal? entryLevelStrength = 1.0m,
        decimal? oppDistancePct = null,
        decimal? oppStrength = null)
    {
        if (bias == TimeframeBias.Neutral) return EntryQuality.Poor;

        // ── Step 1: base quality ──────────────────────────────────────────────
        var quality = bias == TimeframeBias.Bullish
            ? EvaluateLevelBasedQuality(isTrendConfirmed, support1, distanceToSupport1Pct, rsiOverbought)
            : EvaluateLevelBasedQuality(isTrendConfirmed, resistance1, distanceToResistance1Pct, rsiOversold);

        // ── Step 2–5: downgrade rules ─────────────────────────────────────────
        bool hasEmaConflict = HasEmaConflict(bias, isAboveEma20, isAboveEma50);

        quality = ApplyVolumeRule(quality, volumeRatio);
        quality = ApplyEmaRule(quality, bias, isAboveEma20, isAboveEma50);
        quality = ApplySnapshotFreshnessRule(quality, snapshotIsFresh, volumeRatio);
        quality = ApplyMarketRegimeRule(quality, marketRegime, volumeRatio, hasEmaConflict);
        quality = ApplyEntryLevelStrengthRule(quality, entryLevelStrength);
        quality = ApplyOppositeLevelRule(quality, oppDistancePct, oppStrength);

        return quality;
    }

    // ─── Downgrade rules ─────────────────────────────────────────────────────

    /// <summary>
    /// Понижает quality при низком объёме.<br/>
    /// &lt; 0.25 → Poor; &lt; 0.5 → не выше Fair; null → не выше Fair (консервативно).
    /// </summary>
    internal static EntryQuality ApplyVolumeRule(EntryQuality quality, decimal? volumeRatio)
    {
        if (volumeRatio is null) return CapAt(quality, EntryQuality.Fair);
        if (volumeRatio < VeryLowVolumeThreshold) return EntryQuality.Poor;
        if (volumeRatio < LowVolumeThreshold) return CapAt(quality, EntryQuality.Fair);
        return quality;
    }

    /// <summary>
    /// Понижает quality при EMA-конфликте с bias.<br/>
    /// Bullish: цена ниже обеих EMA → Poor; ниже одной → не выше Fair.<br/>
    /// Bearish: цена выше обеих EMA → Poor; выше одной → не выше Fair.
    /// </summary>
    internal static EntryQuality ApplyEmaRule(
        EntryQuality quality, TimeframeBias bias, bool isAboveEma20, bool isAboveEma50)
    {
        if (bias == TimeframeBias.Neutral) return quality;

        if (bias == TimeframeBias.Bullish)
        {
            int conflictCount = (isAboveEma20 ? 0 : 1) + (isAboveEma50 ? 0 : 1);
            return conflictCount switch
            {
                2 => EntryQuality.Poor,
                1 => CapAt(quality, EntryQuality.Fair),
                _ => quality,
            };
        }

        // Bearish: конфликт — когда цена выше EMA
        {
            int conflictCount = (isAboveEma20 ? 1 : 0) + (isAboveEma50 ? 1 : 0);
            return conflictCount switch
            {
                2 => EntryQuality.Poor,
                1 => CapAt(quality, EntryQuality.Fair),
                _ => quality,
            };
        }
    }

    /// <summary>
    /// Понижает quality при устаревшем снапшоте.<br/>
    /// !fresh → не выше Fair; !fresh + low volume → Poor.
    /// </summary>
    internal static EntryQuality ApplySnapshotFreshnessRule(
        EntryQuality quality, bool snapshotIsFresh, decimal? volumeRatio)
    {
        if (snapshotIsFresh) return quality;

        bool lowVolume = volumeRatio is null || volumeRatio < LowVolumeThreshold;
        return lowVolume ? EntryQuality.Poor : CapAt(quality, EntryQuality.Fair);
    }

    /// <summary>
    /// Понижает quality при нейтральном рыночном режиме.<br/>
    /// Neutral + (low volume || EMA-конфликт) → Poor; Neutral → не выше Fair.
    /// </summary>
    internal static EntryQuality ApplyMarketRegimeRule(
        EntryQuality quality, string marketRegime, decimal? volumeRatio, bool hasEmaConflict)
    {
        if (marketRegime != MarketRegimes.Neutral) return quality;

        bool lowVolume = volumeRatio is null || volumeRatio < LowVolumeThreshold;
        return (lowVolume || hasEmaConflict) ? EntryQuality.Poor : CapAt(quality, EntryQuality.Fair);
    }

    /// <summary>
    /// Понижает quality при слабом или неизвестном уровне входа.<br/>
    /// Weak или null → не выше Fair.
    /// </summary>
    internal static EntryQuality ApplyEntryLevelStrengthRule(EntryQuality quality, decimal? strength)
    {
        if (strength is null || strength <= WeakStrengthMax)
            return CapAt(quality, EntryQuality.Fair);
        return quality;
    }

    /// <summary>
    /// Понижает quality при наличии близкого противоположного уровня (препятствие перед ценой).<br/>
    /// dist &lt; <see cref="NearOppositeThreshold"/> → <c>Good</c> запрещён (cap Fair).<br/>
    /// dist &lt; <see cref="CloseOppositeThreshold"/> + Moderate/Strong → Poor.
    /// </summary>
    internal static EntryQuality ApplyOppositeLevelRule(
        EntryQuality quality, decimal? oppDistancePct, decimal? oppStrength)
    {
        if (oppDistancePct is null) return quality;

        var dist = oppDistancePct.Value;
        if (dist >= NearOppositeThreshold) return quality;

        var cat = ClassifyStrength(oppStrength);
        if (dist < CloseOppositeThreshold &&
            cat is LevelStrengthCategory.Moderate or LevelStrengthCategory.Strong)
            return EntryQuality.Poor;

        return CapAt(quality, EntryQuality.Fair);
    }

    // ─── Core quality formula ────────────────────────────────────────────────

    /// <summary>
    /// Базовое качество входа по уровню, дистанции и состоянию RSI.
    /// </summary>
    private static EntryQuality EvaluateLevelBasedQuality(
        bool isTrendConfirmed,
        decimal? level,
        decimal? distancePct,
        bool rsiExtreme)
    {
        if (level is null) return EntryQuality.Poor;
        if (rsiExtreme) return EntryQuality.Poor;
        if (distancePct is not { } dist || dist == 0m) return EntryQuality.Poor;
        if (dist > FairMaxDistance) return EntryQuality.Poor;

        if (isTrendConfirmed && dist <= GoodMaxDistance)
            return EntryQuality.Good;

        return EntryQuality.Fair;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает <c>true</c>, если текущее положение цены относительно EMA конфликтует с bias.
    /// </summary>
    private static bool HasEmaConflict(TimeframeBias bias, bool isAboveEma20, bool isAboveEma50)
    {
        if (bias == TimeframeBias.Bullish) return !isAboveEma20 || !isAboveEma50;
        if (bias == TimeframeBias.Bearish) return isAboveEma20 || isAboveEma50;
        return false;
    }

    /// <summary>Классифицирует нормализованную силу уровня [0, 1].</summary>
    internal static LevelStrengthCategory ClassifyStrength(decimal? strength) =>
        strength switch
        {
            null => LevelStrengthCategory.Unknown,
            <= WeakStrengthMax => LevelStrengthCategory.Weak,
            < ModerateStrengthMax => LevelStrengthCategory.Moderate,
            _ => LevelStrengthCategory.Strong,
        };

    // ─── Cap helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ограничивает качество сверху: возвращает <paramref name="quality"/> если оно не выше
    /// <paramref name="maxQuality"/>, иначе — <paramref name="maxQuality"/>.
    /// </summary>
    internal static EntryQuality CapAt(EntryQuality quality, EntryQuality maxQuality)
        => quality < maxQuality ? maxQuality : quality;
}

/// <summary>Категория силы уровня (support / resistance).</summary>
internal enum LevelStrengthCategory { Unknown, Weak, Moderate, Strong }
