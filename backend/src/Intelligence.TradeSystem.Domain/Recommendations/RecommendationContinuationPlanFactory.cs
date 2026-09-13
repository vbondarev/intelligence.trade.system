using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Recommendations;

internal static class RecommendationContinuationPlanFactory
{
    public static RecommendationContinuationPlan Create(
        PositionAssessment assessment,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        PolicyDefinition policy,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(addDecision);
        ArgumentNullException.ThrowIfNull(policy);

        var invalidation = new List<RecommendationContinuationCondition>
        {
            new DataQualityCondition(
                RecommendationContinuationConditionScope.Recommendation,
                AssessmentDataQuality.FreshCompleteReliable),
            new SafetyStateCondition(
                RecommendationContinuationConditionScope.Recommendation,
                AssessmentSafetyState.Allowed),
            new PolicyIdentityCondition(
                RecommendationContinuationConditionScope.Recommendation,
                policy.Identity),
            new RecommendationExpiryCondition(validUntil)
        };
        var reevaluation = new List<RecommendationContinuationCondition>();

        AddActionConditions(assessment, action, policy, invalidation, reevaluation);
        if (addDecision.Decision == AddDecision.AddAllowed)
            AddAllowedConditions(addDecision, policy, invalidation, reevaluation);
        else
            AddNonAllowedReevaluationConditions(assessment, action, reevaluation);

        var nextEvaluationAt = createdAt.Add(
            policy.ReevaluationProfile.EffectiveInterval(
                action.Action,
                addDecision.Decision == AddDecision.AddAllowed));
        if (nextEvaluationAt > validUntil)
            nextEvaluationAt = validUntil;

        return new(
            invalidation,
            reevaluation,
            createdAt,
            validUntil,
            nextEvaluationAt);
    }

    private static void AddActionConditions(
        PositionAssessment assessment,
        RecommendedActionDecision action,
        PolicyDefinition policy,
        List<RecommendationContinuationCondition> invalidation,
        List<RecommendationContinuationCondition> reevaluation)
    {
        var result = assessment.Result;
        var scope = RecommendationContinuationConditionScope.Action;

        switch (action.Action)
        {
            case PositionAction.Hold:
                AddHoldConditions(result, assessment, invalidation, reevaluation);
                break;
            case PositionAction.Watch:
                AddWatchConditions(result, assessment, reevaluation);
                break;
            case PositionAction.Close:
                if (action.ReasonCodes.Contains(ReasonCode.LiquidationNearby))
                {
                    reevaluation.Add(new LiquidationStateCondition(scope, AssessmentLiquidationState.Near));
                }
                else
                {
                    reevaluation.Add(new TrendAlignmentCondition(scope, PositionTrendAlignment.Adverse));
                    reevaluation.Add(new PnlThresholdCondition(
                        scope,
                        RecommendationPnlComparison.AtMost,
                        policy.CloseLossThreshold));
                }

                break;
            case PositionAction.Reduce:
                reevaluation.Add(new TrendAlignmentCondition(scope, PositionTrendAlignment.Adverse));
                reevaluation.Add(new PnlThresholdCondition(
                    scope,
                    RecommendationPnlComparison.AtMost,
                    policy.ReduceLossThreshold));
                break;
            case PositionAction.TakePartialProfit:
                reevaluation.Add(new PnlThresholdCondition(
                    scope,
                    RecommendationPnlComparison.AtLeast,
                    policy.TakePartialProfitThreshold));
                reevaluation.Add(new MomentumExhaustionCondition(scope, true));
                reevaluation.Add(new OpposingLevelCondition(
                    scope,
                    result.PositionSide == PositionSide.Long
                        ? RecommendationOpposingLevel.ResistanceNearby
                        : RecommendationOpposingLevel.SupportNearby));
                break;
            case PositionAction.MoveStop:
                reevaluation.Add(new PnlThresholdCondition(
                    scope,
                    RecommendationPnlComparison.AtLeast,
                    policy.ProtectProfitThreshold));
                reevaluation.Add(new StopAvailabilityCondition(scope, true));
                reevaluation.Add(new StopProtectionCondition(scope, false));
                break;
            case PositionAction.ProtectProfit:
                reevaluation.Add(new PnlThresholdCondition(
                    scope,
                    RecommendationPnlComparison.AtLeast,
                    policy.ProtectProfitThreshold));
                reevaluation.Add(new StopProtectionCondition(scope, false));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Action, "Action must be defined.");
        }
    }

    private static void AddHoldConditions(
        PositionAssessmentResult result,
        PositionAssessment assessment,
        List<RecommendationContinuationCondition> invalidation,
        List<RecommendationContinuationCondition> reevaluation)
    {
        var scope = RecommendationContinuationConditionScope.Action;
        var lowVolume = assessment.ReasonCodes.Contains(ReasonCode.LowVolume);
        var stopAvailable = result.Stop.StopPrice.HasValue;

        invalidation.Add(new TrendAlignmentCondition(scope, PositionTrendAlignment.Aligned));
        invalidation.Add(new MomentumReliabilityCondition(scope, true));
        invalidation.Add(new MomentumAvailabilityCondition(scope, true));
        invalidation.Add(new LiquidationStateCondition(scope, AssessmentLiquidationState.Far));
        invalidation.Add(new StopProtectionCondition(scope, true));
        reevaluation.Add(new TrendAlignmentCondition(scope, PositionTrendAlignment.Aligned));
        reevaluation.Add(new MomentumReliabilityCondition(scope, true));
        reevaluation.Add(new MomentumAvailabilityCondition(scope, true));
        reevaluation.Add(new LowVolumeCondition(scope, lowVolume));
        reevaluation.Add(new StopAvailabilityCondition(scope, stopAvailable));
        reevaluation.Add(new StopProtectionCondition(scope, true));
        reevaluation.Add(new LiquidationStateCondition(scope, AssessmentLiquidationState.Far));
    }

    private static void AddWatchConditions(
        PositionAssessmentResult result,
        PositionAssessment assessment,
        List<RecommendationContinuationCondition> reevaluation)
    {
        var scope = RecommendationContinuationConditionScope.Action;
        reevaluation.Add(new TrendAlignmentCondition(scope, result.Trend.PositionAlignment));
        reevaluation.Add(new MomentumReliabilityCondition(scope, result.Momentum.IsReliable));
        reevaluation.Add(new MomentumStateCondition(scope, result.Momentum.State));
        reevaluation.Add(new MomentumAvailabilityCondition(
            scope,
            result.Momentum.State != AssessmentMomentumState.Unavailable));
        reevaluation.Add(new StopAvailabilityCondition(scope, result.Stop.StopPrice.HasValue));
        reevaluation.Add(new StopProtectionCondition(
            scope,
            result.Stop.State == AssessmentStopState.Protective));
        reevaluation.Add(new LiquidationStateCondition(scope, result.Liquidation.State));
        reevaluation.Add(new LowVolumeCondition(
            scope,
            assessment.ReasonCodes.Contains(ReasonCode.LowVolume)));
        reevaluation.Add(new DataQualityCondition(scope, result.DataQuality.Overall));
    }

    private static void AddAllowedConditions(
        AddDecisionResult addDecision,
        PolicyDefinition policy,
        List<RecommendationContinuationCondition> invalidation,
        List<RecommendationContinuationCondition> reevaluation)
    {
        var scope = RecommendationContinuationConditionScope.AddDecision;
        invalidation.Add(new PortfolioRiskDecisionCondition(scope, RiskIncreaseDecision.Allowed));
        invalidation.Add(new TrendAlignmentCondition(scope, PositionTrendAlignment.Aligned));
        invalidation.Add(new MomentumReliabilityCondition(scope, true));
        invalidation.Add(new MomentumStateCondition(scope, AssessmentMomentumState.Normal));
        invalidation.Add(new MomentumExhaustionCondition(scope, false));
        invalidation.Add(new LowVolumeCondition(scope, false));
        invalidation.Add(new LiquidationStateCondition(scope, AssessmentLiquidationState.Far));
        invalidation.Add(new LiquidationDistanceCondition(
            scope,
            policy.AddAllowedLimits.MinimumLiquidationDistancePercent));
        invalidation.Add(new StopProtectionCondition(scope, true));
        invalidation.Add(new AddAllowedCapacityCondition(
            addDecision.MaximumAdditionalPositionValue!.Value,
            addDecision.MaximumAdditionalQuantity));

        reevaluation.Add(new PortfolioRiskDecisionCondition(scope, RiskIncreaseDecision.Allowed));
        reevaluation.Add(new TrendAlignmentCondition(scope, PositionTrendAlignment.Aligned));
        reevaluation.Add(new MomentumReliabilityCondition(scope, true));
        reevaluation.Add(new MomentumStateCondition(scope, AssessmentMomentumState.Normal));
        reevaluation.Add(new MomentumExhaustionCondition(scope, false));
        reevaluation.Add(new LowVolumeCondition(scope, false));
        reevaluation.Add(new LiquidationStateCondition(scope, AssessmentLiquidationState.Far));
        reevaluation.Add(new LiquidationDistanceCondition(
            scope,
            policy.AddAllowedLimits.MinimumLiquidationDistancePercent));
        reevaluation.Add(new StopProtectionCondition(scope, true));
        reevaluation.Add(new AddAllowedCapacityCondition(
            addDecision.MaximumAdditionalPositionValue.Value,
            addDecision.MaximumAdditionalQuantity));
    }

    private static void AddNonAllowedReevaluationConditions(
        PositionAssessment assessment,
        RecommendedActionDecision action,
        List<RecommendationContinuationCondition> reevaluation)
    {
        if (action.Action != PositionAction.Hold)
            return;

        var result = assessment.Result;
        reevaluation.Add(new PortfolioRiskDecisionCondition(
            RecommendationContinuationConditionScope.AddDecision,
            assessment.PortfolioRiskDecision));
        reevaluation.Add(new DataQualityCondition(
            RecommendationContinuationConditionScope.AddDecision,
            result.DataQuality.Overall));
    }
}




