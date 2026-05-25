namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Детерминированный итоговый анализ таймфрейма.</summary>
public sealed record LlmTimeframeSummaryPayload
{
    /// <summary>Направленное смещение: <c>Bullish</c>, <c>Bearish</c> или <c>Neutral</c>.</summary>
    public required string Bias { get; init; }

    /// <summary>
    /// Структурное подтверждение тренда на основе EMA-выравнивания и положения цены относительно EMA200.
    /// <list type="bullet">
    ///   <item><term>Bullish</term> <description><c>emaBullishAlignment == true &amp;&amp; isAboveEma200 == true</c></description></item>
    ///   <item><term>Bearish</term> <description><c>emaBearishAlignment == true &amp;&amp; isAboveEma200 == false</c></description></item>
    ///   <item><term>Sideways / Unknown</term> <description>всегда <c>false</c></description></item>
    /// </list>
    /// Инвариант: <c>true</c> возможен только при <c>Bias != Neutral</c>.
    /// </summary>
    public required bool IsTrendConfirmed { get; init; }

    /// <summary>Состояние моментума: <c>Healthy</c>, <c>Weak</c>, <c>Overextended</c> или <c>Neutral</c>.</summary>
    public required string MomentumState { get; init; }

    /// <summary>Качество точки входа: <c>Good</c>, <c>Fair</c> или <c>Poor</c>.</summary>
    public required string EntryQuality { get; init; }

    /// <summary>Список флагов риска.</summary>
    public required IReadOnlyList<string> RiskFlags { get; init; }
}
