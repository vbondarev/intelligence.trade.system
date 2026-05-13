using Intelligence.TradeSystem.Api.Models.Payloads;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Централизованное вычисление <c>summary.entryQuality</c> V1.
///
/// Whitelist допустимых значений:
/// <list type="bullet">
///   <item><term>Good</term>  <description>Подтверждённый bias, цена близко к релевантному уровню, RSI не экстремальный.</description></item>
///   <item><term>Fair</term>  <description>Bias присутствует, умеренная дистанция до уровня, RSI не экстремальный.</description></item>
///   <item><term>Poor</term>  <description>Нейтральный bias, отсутствие уровня, дальняя дистанция или RSI экстремальный.</description></item>
/// </list>
///
/// Детерминированная формула V1:
/// <code>
/// Neutral  → Poor
/// Bullish  → Poor  if (support1 == null || dist == null || dist == 0 || dist > 1.50 || rsiOverbought)
///            Good  if (confirmed &amp;&amp; 0 &lt; dist &lt;= 0.75 &amp;&amp; !rsiOverbought)
///            Fair  if (0 &lt; dist &lt;= 1.50 &amp;&amp; !rsiOverbought)
///            Poor  (fallback)
/// Bearish  → Poor  if (resistance1 == null || dist == null || dist == 0 || dist > 1.50 || rsiOversold)
///            Good  if (confirmed &amp;&amp; 0 &lt; dist &lt;= 0.75 &amp;&amp; !rsiOversold)
///            Fair  if (0 &lt; dist &lt;= 1.50 &amp;&amp; !rsiOversold)
///            Poor  (fallback)
/// </code>
///
/// Инварианты:
/// - bias == Neutral       →  entryQuality == Poor
/// - support1 == null (bullish)  →  entryQuality == Poor
/// - resistance1 == null (bearish) → entryQuality == Poor
/// - rsiOverbought (bullish) →  entryQuality == Poor
/// - rsiOversold (bearish)   →  entryQuality == Poor
/// - entryQuality == Good    →  isTrendConfirmed == true &amp;&amp; dist &lt;= GoodMaxDistance
/// </summary>
internal static class EntryQualityEvaluator
{
    /// <summary>Максимальная дистанция до уровня для оценки <c>Good</c>.</summary>
    internal const decimal GoodMaxDistance = 0.75m;

    /// <summary>Максимальная дистанция до уровня для оценки <c>Fair</c>.</summary>
    internal const decimal FairMaxDistance = 1.50m;

    /// <summary>
    /// Вычисляет качество точки входа на основе bias, подтверждения тренда,
    /// близости к релевантному уровню и состояния RSI.
    /// </summary>
    public static EntryQuality Evaluate(
        TimeframeBias bias,
        bool          isTrendConfirmed,
        decimal?      support1,
        decimal?      distanceToSupport1Pct,
        bool          rsiOverbought,
        decimal?      resistance1,
        decimal?      distanceToResistance1Pct,
        bool          rsiOversold)
    {
        if (bias == TimeframeBias.Neutral) return EntryQuality.Poor;

        if (bias == TimeframeBias.Bullish)
            return EvaluateBullish(isTrendConfirmed, support1, distanceToSupport1Pct, rsiOverbought);

        // Bearish
        return EvaluateBearish(isTrendConfirmed, resistance1, distanceToResistance1Pct, rsiOversold);
    }

    // ─── Bullish ─────────────────────────────────────────────────────────────

    private static EntryQuality EvaluateBullish(
        bool     isTrendConfirmed,
        decimal? support1,
        decimal? distToSupport1,
        bool     rsiOverbought)
    {
        // Poor: нет уровня, дистанция отсутствует/нулевая, слишком далеко или RSI перекуплен
        if (support1 is null)                                            return EntryQuality.Poor;
        if (rsiOverbought)                                               return EntryQuality.Poor;
        if (distToSupport1 is not { } dist || dist == 0m)               return EntryQuality.Poor;
        if (dist > FairMaxDistance)                                      return EntryQuality.Poor;

        // Good: подтверждённый тренд + цена близко к support
        if (isTrendConfirmed && dist <= GoodMaxDistance)
            return EntryQuality.Good;

        // Fair: дистанция приемлемая (≤ FairMaxDistance), без избыточных ограничений
        return EntryQuality.Fair;
    }

    // ─── Bearish ─────────────────────────────────────────────────────────────

    private static EntryQuality EvaluateBearish(
        bool     isTrendConfirmed,
        decimal? resistance1,
        decimal? distToResistance1,
        bool     rsiOversold)
    {
        // Poor: нет уровня, дистанция отсутствует/нулевая, слишком далеко или RSI перепродан
        if (resistance1 is null)                                         return EntryQuality.Poor;
        if (rsiOversold)                                                 return EntryQuality.Poor;
        if (distToResistance1 is not { } dist || dist == 0m)            return EntryQuality.Poor;
        if (dist > FairMaxDistance)                                      return EntryQuality.Poor;

        // Good: подтверждённый тренд + цена близко к resistance
        if (isTrendConfirmed && dist <= GoodMaxDistance)
            return EntryQuality.Good;

        // Fair: дистанция приемлемая (≤ FairMaxDistance)
        return EntryQuality.Fair;
    }
}

