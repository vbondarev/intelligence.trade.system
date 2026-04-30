using Intelligence.TradeSystem.Api.Models.Payloads;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Результат построения summary для одного таймфрейма.
/// Все поля вычислены централизованно в <c>LlmTimeframeSummaryBuilder</c> —
/// гарантированно согласованы между собой.
/// </summary>
internal sealed record LlmTimeframeSummaryResult
{
    /// <summary>Метка силы тренда. Зависит только от <c>Trend</c> и <c>TrendStrengthScore</c>.</summary>
    public required TrendStrengthLabel TrendStrengthLabel { get; init; }

    /// <summary>Направленность таймфрейма. Инвариант: Sideways/Unknown → Neutral.</summary>
    public required TimeframeBias Bias { get; init; }

    /// <summary>
    /// Структурное подтверждение тренда.
    /// Инвариант: <c>true</c> только при <c>Bias ≠ Neutral</c>.
    /// </summary>
    public required bool IsTrendConfirmed { get; init; }

    /// <summary>
    /// Качество моментума.
    /// Инвариант: <see cref="Payloads.MomentumState.Healthy"/> → IsTrendConfirmed == true &amp;&amp; Bias ≠ Neutral.
    /// </summary>
    public required MomentumState MomentumState { get; init; }

    /// <summary>Качество точки входа. Зависит от Bias, IsTrendConfirmed и дистанций до уровней.</summary>
    public required EntryQuality EntryQuality { get; init; }

    /// <summary>Список активных флагов риска для данного таймфрейма.</summary>
    public required IReadOnlyList<string> RiskFlags { get; init; }
}



