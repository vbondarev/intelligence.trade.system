using System.Diagnostics.CodeAnalysis;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Чистая детерминированная политика. Не выполняет IO и не пересчитывает признаки assessment.
/// </summary>
public sealed class RecommendationPolicy
{
    [SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "The policy is registered as a stateless singleton service.")]
    public RecommendationPolicyEvaluation Evaluate(
        PositionAssessment assessment,
        PolicyDefinition policyDefinition,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(policyDefinition);

        if (!assessment.IsValidAt(asOf))
            throw new ArgumentException("Recommendation evaluation must occur within assessment validity.", nameof(asOf));

        var validUntil = Min(
            asOf.Add(policyDefinition.ValidityPeriod),
            assessment.ValidUntil);

        if (RecommendationActionPredicates.IsSafetyBlocked(assessment))
        {
            var action = CreateActionDecision(
                PositionAction.Watch,
                policyDefinition,
                [ReasonCode.RecommendationLimitedByDataQuality]);
            var safetyAddDecision = new AddDecisionResult(
                AddDecision.DoNotAdd,
                [ReasonCode.RiskIncreaseBlockedByDataQuality],
                null,
                null,
                null);
            return new(
                policyDefinition.Identity,
                action,
                safetyAddDecision,
                RecommendationContinuationPlanFactory.Create(
                    assessment,
                    action,
                    safetyAddDecision,
                    policyDefinition,
                    asOf,
                    validUntil),
                asOf,
                validUntil,
                GetInheritedReasonCodes(assessment));
        }

        if (assessment.InputVersions.BasePolicyConfigurationIdentity != policyDefinition.Identity)
            throw new InvalidOperationException(
                "Position assessment and recommendation policy identities do not match.");

        var actionDecision = EvaluateAction(assessment, policyDefinition);
        var addDecision = EvaluateAddDecision(
            assessment,
            policyDefinition,
            actionDecision.Action);
        return new(
            policyDefinition.Identity,
            actionDecision,
            addDecision,
            RecommendationContinuationPlanFactory.Create(
                assessment,
                actionDecision,
                addDecision,
                policyDefinition,
                asOf,
                validUntil),
            asOf,
            validUntil,
            GetInheritedReasonCodes(assessment));
    }

    private static RecommendedActionDecision EvaluateAction(
        PositionAssessment assessment,
        PolicyDefinition policyDefinition)
    {
        var result = assessment.Result;

        if (RecommendationActionPredicates.IsCloseRequired(assessment, policyDefinition))
        {
            IEnumerable<ReasonCode> reasons = result.Liquidation.State == AssessmentLiquidationState.Near
                ? [ReasonCode.LiquidationNearby, ReasonCode.CloseConditionMet]
                : [ReasonCode.TrendAdverse, ReasonCode.PnlNegative, ReasonCode.CloseConditionMet];
            return CreateActionDecision(PositionAction.Close, policyDefinition, reasons);
        }

        if (RecommendationActionPredicates.IsReduceRequired(assessment, policyDefinition))
            return CreateActionDecision(
                PositionAction.Reduce,
                policyDefinition,
                [ReasonCode.TrendAdverse, ReasonCode.PnlNegative, ReasonCode.LossReductionConditionMet]);

        if (RecommendationActionPredicates.IsTakePartialProfitRequired(assessment, policyDefinition))
        {
            var opposingLevel = result.PositionSide == PositionSide.Long
                ? ReasonCode.ResistanceNearby
                : ReasonCode.SupportNearby;
            return CreateActionDecision(
                PositionAction.TakePartialProfit,
                policyDefinition,
                [ReasonCode.PnlPositive, ReasonCode.MomentumExhaustion, opposingLevel,
                 ReasonCode.PartialProfitConditionMet]);
        }

        if (RecommendationActionPredicates.IsMoveStopRequired(assessment, policyDefinition))
            return CreateActionDecision(
                PositionAction.MoveStop,
                policyDefinition,
                [ReasonCode.PnlPositive, ReasonCode.StopNotProtectingProfit, ReasonCode.MoveStopConditionMet]);

        if (RecommendationActionPredicates.IsProtectProfitRequired(assessment, policyDefinition))
        {
            var stopReason = result.Stop.StopPrice.HasValue
                ? ReasonCode.StopUnknown
                : ReasonCode.StopMissing;
            return CreateActionDecision(
                PositionAction.ProtectProfit,
                policyDefinition,
                [ReasonCode.PnlPositive, stopReason, ReasonCode.ProfitProtectionNeeded]);
        }

        if (!RecommendationActionPredicates.CanSafelyHold(assessment))
            return CreateActionDecision(
                PositionAction.Watch,
                policyDefinition,
                BuildWatchReasons(assessment));

        return CreateActionDecision(PositionAction.Hold, policyDefinition, [ReasonCode.TrendAligned]);
    }

    private static AddDecisionResult EvaluateAddDecision(
        PositionAssessment assessment,
        PolicyDefinition policyDefinition,
        PositionAction action)
    {
        var result = assessment.Result;
        var reasons = new List<ReasonCode>();

        if (action != PositionAction.Hold)
            reasons.Add(ReasonCode.AddBlockedByAction);
        if (assessment.PortfolioRiskDecision != RiskIncreaseDecision.Allowed)
            reasons.Add(ReasonCode.AddBlockedByPortfolioRisk);
        if (result.Liquidation.State != AssessmentLiquidationState.Far)
            reasons.Add(result.Liquidation.State == AssessmentLiquidationState.Near
                ? ReasonCode.LiquidationNearby
                : ReasonCode.AddBlockedByLiquidation);
        if (result.Liquidation.DistanceFromCurrentPercent is null ||
            result.Liquidation.DistanceFromCurrentPercent.Value <
            policyDefinition.AddAllowedLimits.MinimumLiquidationDistancePercent)
            reasons.Add(ReasonCode.AddBlockedByLiquidation);
        if (result.Trend.PositionAlignment != PositionTrendAlignment.Aligned)
            reasons.Add(ReasonCode.AddBlockedByTrend);
        if (!result.Momentum.IsReliable || result.Momentum.State != AssessmentMomentumState.Normal)
            reasons.Add(ReasonCode.AddBlockedByMomentum);
        if (result.Momentum.PotentialExhaustion)
            reasons.Add(ReasonCode.MomentumExhaustion);
        if (assessment.ReasonCodes.Contains(ReasonCode.LowVolume))
            reasons.Add(ReasonCode.AddBlockedByVolume);
        if (!result.Stop.StopPrice.HasValue || !ProfitProtectionEvaluator.IsStopProtectingProfit(result))
            reasons.Add(ReasonCode.AddBlockedByStop);

        var capacity = AdditionalPositionCapacityCalculator.Calculate(
            result.PortfolioRisk,
            result.CurrentPrice,
            policyDefinition.AddAllowedLimits);
        reasons.AddRange(capacity.LimitingReasons);

        if (reasons.Count > 0 || capacity.MaximumPositionValue is not > 0m)
        {
            if (capacity.MaximumPositionValue is not > 0m &&
                !reasons.Contains(ReasonCode.AddMaximumSizeUnavailable))
                reasons.Add(ReasonCode.AddMaximumSizeUnavailable);
            return new AddDecisionResult(
                AddDecision.DoNotAdd,
                reasons.Distinct(),
                null,
                null,
                null);
        }

        var conditions = new AddDecisionConditions(
            PositionTrendAlignment.Aligned,
            AssessmentMomentumState.Normal,
            protectiveStopRequired: true,
            policyDefinition.AddAllowedLimits.MinimumLiquidationDistancePercent);
        return new AddDecisionResult(
            AddDecision.AddAllowed,
            [ReasonCode.AddAllowedWithinLimits],
            capacity.MaximumPositionValue,
            capacity.MaximumQuantity,
            conditions);
    }


    private static ReasonCode[] BuildWatchReasons(PositionAssessment assessment)
    {
        var result = assessment.Result;
        var reasons = new List<ReasonCode>();

        switch (result.Trend.PositionAlignment)
        {
            case PositionTrendAlignment.Adverse:
                reasons.Add(ReasonCode.TrendAdverse);
                break;
            case PositionTrendAlignment.FlatOrUnknown:
                reasons.Add(ReasonCode.TrendFlatOrUnknown);
                break;
        }

        if (result.Pnl.PnlPercent is null)
            reasons.Add(ReasonCode.PnlUnavailable);
        if (!result.Momentum.IsReliable || result.Momentum.State == AssessmentMomentumState.Unavailable)
            reasons.Add(ReasonCode.MomentumUnavailable);

        ReasonCode? liquidationReason = result.Liquidation.State switch
        {
            AssessmentLiquidationState.Invalid => ReasonCode.LiquidationInvalid,
            AssessmentLiquidationState.Unavailable => ReasonCode.LiquidationUnavailable,
            AssessmentLiquidationState.Near => ReasonCode.LiquidationNearby,
            _ => (ReasonCode?)null
        };
        if (liquidationReason.HasValue)
            reasons.Add(liquidationReason.Value);

        if (assessment.ReasonCodes.Contains(ReasonCode.LowVolume))
            reasons.Add(ReasonCode.LowVolume);

        if (!result.Stop.StopPrice.HasValue)
            reasons.Add(ReasonCode.StopMissing);
        else if (result.Stop.State == AssessmentStopState.NonProtective)
            reasons.Add(ReasonCode.StopNonProtective);
        else if (result.Stop.State == AssessmentStopState.Unknown)
            reasons.Add(ReasonCode.StopUnknown);

        return reasons
            .Distinct()
            .ToArray();
    }

    private static ReasonCode[] GetInheritedReasonCodes(PositionAssessment assessment) =>
        assessment.ReasonCodes
            .Where(ReasonCodeClassification.IsPortfolioRiskReason)
            .OrderBy(reason => (int)reason)
            .ToArray();

    private static RecommendedActionDecision CreateActionDecision(
        PositionAction action,
        PolicyDefinition policyDefinition,
        IEnumerable<ReasonCode> reasons) =>
        new(
            action,
            policyDefinition.ConfidenceProfiles.For(action),
            policyDefinition.PriorityProfiles.For(action),
            reasons);

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}
