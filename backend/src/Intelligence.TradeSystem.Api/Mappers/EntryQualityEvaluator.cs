using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Централизованное вычисление <c>summary.entryQuality</c> V1.
///
/// Whitelist допустимых значений:
/// <list type="bullet">
///   <item><term>Good</term>  <description>Подтверждённый bias, цена близко к релевантному уровню (в т.ч. ретест), RSI не экстремальный, объём нормальный, EMA подтверждают направление, снапшот свежий, режим не Neutral.</description></item>
///   <item><term>Fair</term>  <description>Bias присутствует, умеренная дистанция до уровня, RSI не экстремальный. Допустим при небольших ограничениях.</description></item>
///   <item><term>Poor</term>  <description>Нейтральный bias, отсутствие уровня, дальняя дистанция, RSI экстремальный, низкий объём, EMA-конфликт, устаревший снапшот или neutral режим без подтверждений.</description></item>
/// </list>
///
/// Детерминированная формула V1 (baseQuality + downgrade rules):
/// <code>
/// Neutral  → Poor
/// Bullish/Bearish:
///   1. baseQuality = EvaluateLevelBasedQuality(...)
///   2. ApplyVolumeRule:        volumeRatio &lt; 0.25 → Poor; &lt; 0.5 → cap Fair; null → cap Fair
///   3. ApplyEmaRule:           EMA-конфликт с bias → Poor (оба) / cap Fair (один); null → conflict
///   4. ApplySnapshotFreshnessRule: !fresh → cap Fair; !fresh &amp;&amp; lowVolume → Poor
///   5. ApplyMarketRegimeRule:  null/empty → cap Fair; Neutral + (lowVolume || EMA-конфликт) → Poor; Neutral → cap Fair
///   6. ApplyEntryLevelStrengthRule: Weak/null → cap Fair
///   7. ApplyOppositeLevelRule: negative dist → ignored; &lt; 0.30% → cap Fair; &lt; 0.15% + Moderate/Strong → Poor
/// </code>
///
/// Инварианты:
/// - bias == Neutral                    →  entryQuality == Poor
/// - support1 == null (bullish)         →  entryQuality == Poor
/// - resistance1 == null (bearish)      →  entryQuality == Poor
/// - rsiOverbought (bullish)            →  entryQuality == Poor
/// - rsiOversold (bearish)              →  entryQuality == Poor
/// - distancePct == null || &lt; 0      →  entryQuality == Poor (absent/wrong-side)
/// - distancePct == 0                   →  ретест уровня; Good/Fair возможны при прочих подтверждениях
/// - entryQuality == Good               →  isTrendConfirmed == true &amp;&amp; dist &lt;= GoodMaxDistance
/// - entryQuality == Good               →  volumeRatio &gt;= 0.5 (или недоступен → не Good)
/// - entryQuality == Good               →  EMA подтверждают bias (null EMA = conflict → не Good)
/// - entryQuality == Good               →  snapshotIsFresh == true
/// - entryQuality == Good               →  marketRegime != Neutral (null/empty → cap Fair)
/// - entryQuality == Good               →  entryLevelStrength &gt; WeakStrengthMax (null → cap Fair)
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
    /// <param name="isAboveEma20">
    /// <c>true</c> — цена выше EMA20; <c>false</c> — ниже; <c>null</c> — EMA недоступен.
    /// Неизвестное значение трактуется консервативно: считается конфликтом с bias.
    /// </param>
    /// <param name="isAboveEma50">Аналогично <paramref name="isAboveEma20"/> для EMA50.</param>
    /// <param name="marketRegime">
    /// Рыночный режим (<see cref="MarketRegimes"/>). Сравнение регистронезависимо, пробелы обрезаются.
    /// <c>null</c> или пустая строка → неизвестный режим; трактуется консервативно: cap Fair.
    /// </param>
    /// <param name="entryLevelStrength">
    /// Нормализованная сила уровня входа [0, 1].
    /// <c>null</c> (по умолчанию) → неизвестная сила; cap Fair (Good запрещён).
    /// </param>
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
        bool? isAboveEma20,
        bool? isAboveEma50,
        bool snapshotIsFresh,
        string? marketRegime,
        decimal? entryLevelStrength = null,
        decimal? oppDistancePct = null,
        decimal? oppStrength = null)
    {
        if (bias == TimeframeBias.Neutral) return EntryQuality.Poor;

        // ── Step 1: base quality ──────────────────────────────────────────────
        var quality = bias == TimeframeBias.Bullish
            ? EvaluateLevelBasedQuality(isTrendConfirmed, support1, distanceToSupport1Pct, rsiOverbought)
            : EvaluateLevelBasedQuality(isTrendConfirmed, resistance1, distanceToResistance1Pct, rsiOversold);

        // ── Step 2–7: downgrade rules ─────────────────────────────────────────
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
    /// Bearish: цена выше обеих EMA → Poor; выше одной → не выше Fair.<br/>
    /// <c>null</c> (неизвестное положение) трактуется консервативно — считается конфликтом.
    /// </summary>
    internal static EntryQuality ApplyEmaRule(
        EntryQuality quality, TimeframeBias bias, bool? isAboveEma20, bool? isAboveEma50)
    {
        if (bias == TimeframeBias.Neutral) return quality;

        if (bias == TimeframeBias.Bullish)
        {
            // null = unknown position = conservative = not confirmed above EMA = conflict
            int conflictCount = (isAboveEma20 == true ? 0 : 1) + (isAboveEma50 == true ? 0 : 1);
            return conflictCount switch
            {
                2 => EntryQuality.Poor,
                1 => CapAt(quality, EntryQuality.Fair),
                _ => quality,
            };
        }

        // Bearish: конфликт — когда цена выше EMA.
        // null = unknown = conservative = not confirmed below EMA = conflict.
        {
            int conflictCount = (isAboveEma20 == false ? 0 : 1) + (isAboveEma50 == false ? 0 : 1);
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
    /// Понижает quality при нейтральном или неизвестном рыночном режиме.<br/>
    /// null/empty → неизвестный режим → cap Fair.<br/>
    /// Neutral + (low volume || EMA-конфликт) → Poor; Neutral → не выше Fair.<br/>
    /// Сравнение регистронезависимо (OrdinalIgnoreCase), пробелы обрезаются.
    /// </summary>
    internal static EntryQuality ApplyMarketRegimeRule(
        EntryQuality quality, string? marketRegime, decimal? volumeRatio, bool hasEmaConflict)
    {
        // Unknown regime: conservative cap — Good forbidden, max Fair.
        if (string.IsNullOrWhiteSpace(marketRegime))
            return CapAt(quality, EntryQuality.Fair);

        if (!string.Equals(marketRegime.Trim(), MarketRegimes.Neutral, StringComparison.OrdinalIgnoreCase))
            return quality;

        bool lowVolume = volumeRatio is null || volumeRatio < LowVolumeThreshold;
        return (lowVolume || hasEmaConflict) ? EntryQuality.Poor : CapAt(quality, EntryQuality.Fair);
    }

    /// <summary>
    /// Понижает quality при слабом или неизвестном уровне входа.<br/>
    /// Weak (≤ 0.35) или null → не выше Fair.
    /// </summary>
    internal static EntryQuality ApplyEntryLevelStrengthRule(EntryQuality quality, decimal? strength)
    {
        if (strength is null || strength <= WeakStrengthMax)
            return CapAt(quality, EntryQuality.Fair);
        return quality;
    }

    /// <summary>
    /// Понижает quality при наличии близкого противоположного уровня (препятствие перед ценой).<br/>
    /// Отрицательная дистанция означает, что уровень находится на неправильной стороне цены — игнорируется.<br/>
    /// dist &lt; <see cref="NearOppositeThreshold"/> → <c>Good</c> запрещён (cap Fair).<br/>
    /// dist &lt; <see cref="CloseOppositeThreshold"/> + Moderate/Strong → Poor.
    /// </summary>
    internal static EntryQuality ApplyOppositeLevelRule(
        EntryQuality quality, decimal? oppDistancePct, decimal? oppStrength)
    {
        if (oppDistancePct is null) return quality;

        var dist = oppDistancePct.Value;

        // Negative distance: level is behind the trade direction (wrong side of price) — not an obstacle.
        if (dist < 0m) return quality;

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
    /// distancePct == 0 означает ретест уровня — допустимо для Good/Fair при прочих условиях.
    /// distancePct == null или &lt; 0 означает «данные недоступны / уровень на неверной стороне» → Poor.
    /// </summary>
    private static EntryQuality EvaluateLevelBasedQuality(
        bool isTrendConfirmed,
        decimal? level,
        decimal? distancePct,
        bool rsiExtreme)
    {
        if (level is null) return EntryQuality.Poor;
        if (rsiExtreme) return EntryQuality.Poor;
        // null → data absent; negative → wrong side. Zero is valid (retest at the level).
        if (distancePct is not { } dist || dist < 0m) return EntryQuality.Poor;
        if (dist > FairMaxDistance) return EntryQuality.Poor;

        if (isTrendConfirmed && dist <= GoodMaxDistance)
            return EntryQuality.Good;

        return EntryQuality.Fair;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает <c>true</c>, если текущее положение цены относительно EMA конфликтует с bias.
    /// <c>null</c> (неизвестное значение) трактуется консервативно: всегда считается конфликтом.
    /// </summary>
    private static bool HasEmaConflict(TimeframeBias bias, bool? isAboveEma20, bool? isAboveEma50)
    {
        if (bias == TimeframeBias.Bullish) return isAboveEma20 != true || isAboveEma50 != true;
        if (bias == TimeframeBias.Bearish) return isAboveEma20 != false || isAboveEma50 != false;
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
