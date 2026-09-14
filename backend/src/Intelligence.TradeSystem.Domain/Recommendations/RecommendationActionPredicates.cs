using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Recommendations;

internal static class RecommendationActionPredicates
{
    public static bool IsCloseRequired(PositionAssessment assessment, PolicyDefinition policy)
    {
        if (IsSafetyBlocked(assessment))
            return false;

        var result = assessment.Result;
        return result.Liquidation.State == AssessmentLiquidationState.Near ||
            result.Trend.PositionAlignment == PositionTrendAlignment.Adverse &&
            result.Pnl.PnlPercent <= policy.CloseLossThreshold;
    }

    public static bool IsReduceRequired(
        PositionAssessment assessment,
        PolicyDefinition policy)
    {
        if (IsSafetyBlocked(assessment) || IsCloseRequired(assessment, policy))
            return false;

        var result = assessment.Result;
        return result.Trend.PositionAlignment == PositionTrendAlignment.Adverse &&
            result.Pnl.PnlPercent <= policy.ReduceLossThreshold;
    }

    public static bool IsTakePartialProfitRequired(
        PositionAssessment assessment,
        PolicyDefinition policy)
    {
        if (IsSafetyBlocked(assessment) ||
            IsCloseRequired(assessment, policy) ||
            IsReduceRequired(assessment, policy))
            return false;

        var result = assessment.Result;
        return IsProfitable(result) &&
            result.Pnl.PnlPercent >= policy.TakePartialProfitThreshold &&
            result.Momentum.PotentialExhaustion &&
            ((result.PositionSide == PositionSide.Long &&
              assessment.ReasonCodes.Contains(ReasonCode.ResistanceNearby)) ||
             (result.PositionSide == PositionSide.Short &&
              assessment.ReasonCodes.Contains(ReasonCode.SupportNearby)));
    }

    public static bool IsMoveStopRequired(
        PositionAssessment assessment,
        PolicyDefinition policy)
    {
        if (IsSafetyBlocked(assessment) ||
            IsCloseRequired(assessment, policy) ||
            IsReduceRequired(assessment, policy) ||
            IsTakePartialProfitRequired(assessment, policy))
            return false;

        var result = assessment.Result;
        return IsProfitable(result) &&
            result.Pnl.PnlPercent >= policy.ProtectProfitThreshold &&
            result.Stop.StopPrice.HasValue &&
            result.Stop.PriceRelativeToEntry != AssessmentPricePosition.Unavailable &&
            !ProfitProtectionEvaluator.IsStopProtectingProfit(result);
    }

    public static bool IsProtectProfitRequired(
        PositionAssessment assessment,
        PolicyDefinition policy)
    {
        if (IsSafetyBlocked(assessment) ||
            IsCloseRequired(assessment, policy) ||
            IsReduceRequired(assessment, policy) ||
            IsTakePartialProfitRequired(assessment, policy) ||
            IsMoveStopRequired(assessment, policy))
            return false;

        var result = assessment.Result;
        return IsProfitable(result) &&
            result.Pnl.PnlPercent >= policy.ProtectProfitThreshold &&
            (!result.Stop.StopPrice.HasValue ||
             result.Stop.PriceRelativeToEntry == AssessmentPricePosition.Unavailable);
    }

    public static bool CanSafelyHold(
        PositionAssessment assessment)
    {
        if (IsSafetyBlocked(assessment))
            return false;

        var result = assessment.Result;
        return result.Trend.PositionAlignment == PositionTrendAlignment.Aligned &&
            result.Liquidation.State == AssessmentLiquidationState.Far &&
            result.Pnl.PnlPercent.HasValue &&
            result.Momentum.IsReliable &&
            result.Momentum.State != AssessmentMomentumState.Unavailable &&
            !assessment.ReasonCodes.Contains(ReasonCode.LowVolume) &&
            result.Stop.StopPrice.HasValue &&
            result.Stop.State == AssessmentStopState.Protective;
    }

    public static bool IsActionRequired(
        PositionAssessment assessment,
        PolicyDefinition policy,
        PositionAction action) =>
        action switch
        {
            PositionAction.Close => IsCloseRequired(assessment, policy),
            PositionAction.Reduce => IsReduceRequired(assessment, policy),
            PositionAction.TakePartialProfit => IsTakePartialProfitRequired(assessment, policy),
            PositionAction.MoveStop => IsMoveStopRequired(assessment, policy),
            PositionAction.ProtectProfit => IsProtectProfitRequired(assessment, policy),
            _ => false
        };

    public static bool IsSafetyBlocked(PositionAssessment assessment) =>
        assessment.Result.IsLegacy ||
        assessment.Result.DataQuality.Overall != AssessmentDataQuality.FreshCompleteReliable ||
        assessment.Result.DataQuality.SafetyState != AssessmentSafetyState.Allowed;

    public static bool IsSafetyBlocked(RecommendationPolicyEvaluation evaluation) =>
        evaluation.Action.Action == PositionAction.Watch &&
        evaluation.AddDecision.Decision == AddDecision.DoNotAdd &&
        evaluation.Action.ReasonCodes.Contains(ReasonCode.RecommendationLimitedByDataQuality) &&
        evaluation.AddDecision.ReasonCodes.Contains(ReasonCode.RiskIncreaseBlockedByDataQuality);

    private static bool IsProfitable(PositionAssessmentResult result) =>
        result.Pnl.UnrealizedPnl > 0m && result.Pnl.PnlPercent > 0m;
}
