using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

public sealed record RecommendationContinuationEvaluationResult(
    bool IsInvalidated,
    bool ShouldReevaluate,
    bool ActionInvalidated,
    bool AddDecisionInvalidated,
    bool IsExpired,
    IReadOnlyList<RecommendationContinuationCondition> TriggeredConditions);

/// <summary>
/// Чистая проверка applicability recommendation. Не выполняет IO, не мутирует aggregate
/// и не создаёт новую recommendation.
/// </summary>
public static class RecommendationContinuationEvaluator
{
    public static RecommendationContinuationEvaluationResult Evaluate(
        Recommendation recommendation,
        PositionAssessment latestAssessment,
        PolicyDefinition currentPolicy,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(latestAssessment);
        ArgumentNullException.ThrowIfNull(currentPolicy);
        if (recommendation.PositionId != latestAssessment.PositionId)
            throw new ArgumentException(
                "Recommendation and latest assessment must reference the same position.",
                nameof(latestAssessment));
        if (asOf < recommendation.CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(asOf), asOf, "Evaluation cannot precede recommendation creation.");
        if (asOf < recommendation.ValidUntil && !latestAssessment.IsValidAt(asOf))
            throw new ArgumentException(
                "Latest assessment must be valid at continuation evaluation time.",
                nameof(latestAssessment));

        if (!recommendation.HasContinuationPlan)
        {
            var unavailable = new ContinuationContextUnavailableCondition();
            var expired = asOf >= recommendation.ValidUntil;
            return new(
                expired || recommendation.AddDecision == AddDecision.AddAllowed,
                true,
                expired,
                recommendation.AddDecision == AddDecision.AddAllowed,
                expired,
                [unavailable]);
        }

        var plan = recommendation.ContinuationPlan!;
        var triggered = new List<RecommendationContinuationCondition>();
        var actionInvalidated = false;
        var addDecisionInvalidated = false;
        var isExpired = asOf >= recommendation.ValidUntil;

        EvaluateHardConditions(
            recommendation,
            latestAssessment,
            currentPolicy,
            asOf,
            triggered,
            ref actionInvalidated,
            ref addDecisionInvalidated);

        EvaluateConditionList(
            plan.InvalidationConditions,
            recommendation,
            latestAssessment,
            currentPolicy,
            triggered,
            isInvalidationList: true,
            ref actionInvalidated,
            ref addDecisionInvalidated);
        EvaluateConditionList(
            plan.ReevaluationConditions,
            recommendation,
            latestAssessment,
            currentPolicy,
            triggered,
            isInvalidationList: false,
            ref actionInvalidated,
            ref addDecisionInvalidated);

        if (isExpired)
        {
            var expiry = plan.InvalidationConditions
                .OfType<RecommendationExpiryCondition>()
                .FirstOrDefault() ?? new RecommendationExpiryCondition(recommendation.ValidUntil);
            AddTriggered(triggered, expiry);
            actionInvalidated = true;
        }

        var scheduled = asOf >= plan.NextEvaluationAt;
        var isInvalidated = actionInvalidated || addDecisionInvalidated;
        return new(
            isInvalidated,
            isInvalidated || scheduled || triggered.Count > 0,
            actionInvalidated,
            addDecisionInvalidated,
            isExpired,
            triggered.AsReadOnly());
    }

    private static void EvaluateHardConditions(
        Recommendation recommendation,
        PositionAssessment latestAssessment,
        PolicyDefinition currentPolicy,
        DateTimeOffset asOf,
        ICollection<RecommendationContinuationCondition> triggered,
        ref bool actionInvalidated,
        ref bool addDecisionInvalidated)
    {
        var result = latestAssessment.Result;
        if (result.IsLegacy ||
            result.DataQuality.Overall != AssessmentDataQuality.FreshCompleteReliable)
        {
            var condition = new DataQualityCondition(
                RecommendationContinuationConditionScope.Recommendation,
                AssessmentDataQuality.FreshCompleteReliable);
            AddTriggered(triggered, condition);
            actionInvalidated = true;
            if (recommendation.AddDecision == AddDecision.AddAllowed)
                addDecisionInvalidated = true;
        }

        if (result.DataQuality.SafetyState != AssessmentSafetyState.Allowed)
        {
            var condition = new SafetyStateCondition(
                RecommendationContinuationConditionScope.Recommendation,
                AssessmentSafetyState.Allowed);
            AddTriggered(triggered, condition);
            actionInvalidated = true;
            if (recommendation.AddDecision == AddDecision.AddAllowed)
                addDecisionInvalidated = true;
        }

        if (recommendation.PolicyIdentity != currentPolicy.Identity)
        {
            var condition = new PolicyIdentityCondition(
                RecommendationContinuationConditionScope.Recommendation,
                currentPolicy.Identity);
            AddTriggered(triggered, condition);
            actionInvalidated = true;
        }

        _ = asOf;
    }

    private static void EvaluateConditionList(
        IReadOnlyList<RecommendationContinuationCondition> conditions,
        Recommendation recommendation,
        PositionAssessment assessment,
        PolicyDefinition policy,
        ICollection<RecommendationContinuationCondition> triggered,
        bool isInvalidationList,
        ref bool actionInvalidated,
        ref bool addDecisionInvalidated)
    {
        foreach (var condition in conditions)
        {
            if (condition is RecommendationExpiryCondition or ContinuationContextUnavailableCondition)
                continue;

            if (!IsTriggered(condition, recommendation, assessment, policy))
                continue;

            AddTriggered(triggered, condition);
            switch (condition.Scope)
            {
                case RecommendationContinuationConditionScope.Action:
                    actionInvalidated |= isInvalidationList;
                    break;
                case RecommendationContinuationConditionScope.AddDecision:
                    if (recommendation.AddDecision == AddDecision.AddAllowed)
                        addDecisionInvalidated = true;
                    break;
                case RecommendationContinuationConditionScope.Recommendation:
                    actionInvalidated = true;
                    if (recommendation.AddDecision == AddDecision.AddAllowed)
                        addDecisionInvalidated = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(conditions),
                        condition.Scope,
                        "Condition scope must be defined.");
            }
        }
    }

    private static bool IsTriggered(
        RecommendationContinuationCondition condition,
        Recommendation recommendation,
        PositionAssessment assessment,
        PolicyDefinition policy)
    {
        var result = assessment.Result;
        return condition switch
        {
            TrendAlignmentCondition value => result.Trend.PositionAlignment != value.RequiredAlignment,
            MomentumReliabilityCondition value => result.Momentum.IsReliable != value.RequiredReliability,
            MomentumStateCondition value => result.Momentum.State != value.RequiredState,
            MomentumAvailabilityCondition value =>
                (result.Momentum.State != AssessmentMomentumState.Unavailable) != value.RequiredAvailability,
            MomentumExhaustionCondition value => result.Momentum.PotentialExhaustion != value.RequiredExhaustion,
            StopProtectionCondition value => IsProtectiveStop(result) != value.RequiredProtective,
            StopAvailabilityCondition value => result.Stop.StopPrice.HasValue != value.RequiredAvailability,
            LiquidationStateCondition value => result.Liquidation.State != value.RequiredState,
            LiquidationDistanceCondition value =>
                result.Liquidation.DistanceFromCurrentPercent is not { } distance ||
                distance < value.MinimumDistancePercent,
            PnlThresholdCondition value => !MeetsPnlThreshold(result.Pnl.PnlPercent, value),
            DataQualityCondition value => result.DataQuality.Overall != value.RequiredQuality,
            SafetyStateCondition value => result.DataQuality.SafetyState != value.RequiredState,
            PortfolioRiskDecisionCondition value => assessment.PortfolioRiskDecision != value.RequiredDecision,
            LowVolumeCondition value => assessment.ReasonCodes.Contains(ReasonCode.LowVolume) != value.RequiredLowVolume,
            PolicyIdentityCondition => recommendation.PolicyIdentity != policy.Identity,
            AddAllowedCapacityCondition value => IsCapacityReduced(assessment, policy, value),
            OpposingLevelCondition value => !HasOpposingLevel(assessment, value.RequiredLevel),
            _ => throw new InvalidOperationException(
                $"Unsupported continuation condition type '{condition.GetType().Name}'.")
        };
    }

    private static bool IsCapacityReduced(
        PositionAssessment assessment,
        PolicyDefinition policy,
        AddAllowedCapacityCondition persistedCapacity)
    {
        var current = AdditionalPositionCapacityCalculator.Calculate(
            assessment.Result.PortfolioRisk,
            assessment.Result.CurrentPrice,
            policy.AddAllowedLimits);
        if (current.MaximumPositionValue is not > 0m ||
            current.MaximumPositionValue < persistedCapacity.MaximumPositionValue)
            return true;
        return persistedCapacity.MaximumQuantity is { } persistedQuantity &&
            (current.MaximumQuantity is not { } currentQuantity || currentQuantity < persistedQuantity);
    }

    private static bool MeetsPnlThreshold(
        decimal? pnl,
        PnlThresholdCondition condition) =>
        pnl is { } value && condition.Comparison switch
        {
            RecommendationPnlComparison.AtLeast => value >= condition.Threshold,
            RecommendationPnlComparison.AtMost => value <= condition.Threshold,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition.Comparison, "PnL comparison must be defined.")
        };

    private static bool HasOpposingLevel(
        PositionAssessment assessment,
        RecommendationOpposingLevel level) =>
        assessment.ReasonCodes.Contains(level == RecommendationOpposingLevel.ResistanceNearby
            ? ReasonCode.ResistanceNearby
            : ReasonCode.SupportNearby);

    private static bool IsProtectiveStop(PositionAssessmentResult result) =>
        result.Stop.State == AssessmentStopState.Protective;

    private static void AddTriggered(
        ICollection<RecommendationContinuationCondition> triggered,
        RecommendationContinuationCondition condition)
    {
        if (!triggered.Contains(condition))
            triggered.Add(condition);
    }
}






