using Intelligence.TradeSystem.Domain.Assessments;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Зафиксированные условия, при которых было разрешено увеличение позиции.
/// Это контекст решения, а не lifecycle-trigger для E.3.
/// </summary>
public sealed record AddDecisionConditions
{
    public AddDecisionConditions(
        PositionTrendAlignment requiredTrendAlignment,
        AssessmentMomentumState requiredMomentumState,
        bool protectiveStopRequired,
        decimal minimumLiquidationDistancePercent)
    {
        if (requiredTrendAlignment != PositionTrendAlignment.Aligned)
            throw new ArgumentOutOfRangeException(
                nameof(requiredTrendAlignment),
                requiredTrendAlignment,
                "AddAllowed requires aligned trend.");
        if (requiredMomentumState != AssessmentMomentumState.Normal)
            throw new ArgumentOutOfRangeException(
                nameof(requiredMomentumState),
                requiredMomentumState,
                "AddAllowed requires normal momentum.");
        if (!protectiveStopRequired)
            throw new ArgumentException("AddAllowed requires a protective stop.", nameof(protectiveStopRequired));
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLiquidationDistancePercent);
        if (minimumLiquidationDistancePercent == 0m)
            throw new ArgumentOutOfRangeException(
                nameof(minimumLiquidationDistancePercent),
                "Minimum liquidation distance must be positive.");

        RequiredTrendAlignment = requiredTrendAlignment;
        RequiredMomentumState = requiredMomentumState;
        ProtectiveStopRequired = protectiveStopRequired;
        MinimumLiquidationDistancePercent = minimumLiquidationDistancePercent;
    }

    public PositionTrendAlignment RequiredTrendAlignment { get; }
    public AssessmentMomentumState RequiredMomentumState { get; }
    public bool ProtectiveStopRequired { get; }
    public decimal MinimumLiquidationDistancePercent { get; }
}
