using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Snapshots;

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
            throw new ArgumentOutOfRangeException(
                nameof(asOf),
                asOf,
                "Evaluation cannot precede recommendation creation.");
        if (asOf < recommendation.ValidUntil && !latestAssessment.IsValidAt(asOf))
            throw new ArgumentException(
                "Latest assessment must be valid at continuation evaluation time.",
                nameof(latestAssessment));

        if (!recommendation.HasContinuationPlan)
            return EvaluateLegacyRecommendation(recommendation, latestAssessment, currentPolicy, asOf);

        var plan = recommendation.ContinuationPlan!;
        var triggered = new List<RecommendationContinuationCondition>();
        var actionInvalidated = false;
        var addDecisionInvalidated = false;
        var isExpired = asOf >= recommendation.ValidUntil;

        EvaluateHardSafety(
            recommendation,
            latestAssessment,
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
                .Single();
            AddTriggered(triggered, expiry);
            actionInvalidated = true;
            if (recommendation.AddDecision == AddDecision.AddAllowed)
                addDecisionInvalidated = true;
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

    private static RecommendationContinuationEvaluationResult EvaluateLegacyRecommendation(
        Recommendation recommendation,
        PositionAssessment assessment,
        PolicyDefinition currentPolicy,
        DateTimeOffset asOf)
    {
        var triggered = new List<RecommendationContinuationCondition>
        {
            new ContinuationContextUnavailableCondition()
        };
        var isExpired = asOf >= recommendation.ValidUntil;
        var policyChanged = recommendation.PolicyIdentity != currentPolicy.Identity;
        var safetyBlocked =
            assessment.Result.IsLegacy ||
            assessment.Result.DataQuality.Overall != AssessmentDataQuality.FreshCompleteReliable ||
            assessment.Result.DataQuality.SafetyState != AssessmentSafetyState.Allowed;
        var actionInvalidated = isExpired || policyChanged;
        var addDecisionInvalidated = recommendation.AddDecision == AddDecision.AddAllowed;

        if (policyChanged)
            AddTriggered(
                triggered,
                new PolicyIdentityCondition(
                    RecommendationContinuationConditionScope.Recommendation,
                    recommendation.PolicyIdentity));
        if (safetyBlocked && recommendation.RecommendedAction != PositionAction.Watch)
        {
            AddTriggered(
                triggered,
                new DataQualityCondition(
                    RecommendationContinuationConditionScope.Recommendation,
                    AssessmentDataQuality.FreshCompleteReliable));
            actionInvalidated = true;
        }
        if (isExpired)
        {
            var expiry = new RecommendationExpiryCondition(recommendation.ValidUntil);
            AddTriggered(triggered, expiry);
        }

        var isInvalidated = actionInvalidated || addDecisionInvalidated;
        return new(
            isInvalidated,
            true,
            actionInvalidated,
            addDecisionInvalidated,
            isExpired,
            triggered.AsReadOnly());
    }

    private static void EvaluateHardSafety(
        Recommendation recommendation,
        PositionAssessment latestAssessment,
        ICollection<RecommendationContinuationCondition> triggered,
        ref bool actionInvalidated,
        ref bool addDecisionInvalidated)
    {
        var plan = recommendation.ContinuationPlan!;
        var hasDegradedRecoveryConditions =
            plan.ReevaluationConditions.OfType<DataQualityCondition>()
                .Any(condition => condition.RequiredQuality != AssessmentDataQuality.FreshCompleteReliable) ||
            plan.ReevaluationConditions.OfType<SafetyStateCondition>()
                .Any(condition => condition.RequiredState != AssessmentSafetyState.Allowed);
        var isSafetyFallback =
            recommendation.RecommendedAction == PositionAction.Watch &&
            recommendation.AddDecision == AddDecision.DoNotAdd &&
            hasDegradedRecoveryConditions &&
            !plan.InvalidationConditions.OfType<DataQualityCondition>()
                .Any(condition => condition.RequiredQuality == AssessmentDataQuality.FreshCompleteReliable) &&
            !plan.InvalidationConditions.OfType<SafetyStateCondition>()
                .Any(condition => condition.RequiredState == AssessmentSafetyState.Allowed);
        if (isSafetyFallback)
            return;

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

            if (condition is AddAllowedCapacityCondition capacity)
            {
                var current = AdditionalPositionCapacityCalculator.Calculate(
                    assessment.Result.PortfolioRisk,
                    assessment.Result.CurrentPrice,
                    policy.AddAllowedLimits);
                if (!CapacityChanged(current, capacity))
                    continue;

                AddTriggered(triggered, condition);
                if (recommendation.AddDecision == AddDecision.AddAllowed &&
                    CapacityReduced(current, capacity))
                    addDecisionInvalidated = true;
                continue;
            }

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
                    actionInvalidated |= isInvalidationList;
                    if (recommendation.AddDecision == AddDecision.AddAllowed &&
                        isInvalidationList)
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
            StopStateCondition value => result.Stop.State != value.RequiredState,
            StopAvailabilityCondition value => result.Stop.StopPrice.HasValue != value.RequiredAvailability,
            StopRelativePositionCondition value => result.Stop.PriceRelativeToEntry != value.RequiredPosition,
            ProfitProtectionCondition value =>
                ProfitProtectionEvaluator.IsStopProtectingProfit(result) != value.RequiredProtection,
            LiquidationStateCondition value => result.Liquidation.State != value.RequiredState,
            LiquidationDistanceCondition value =>
                result.Liquidation.DistanceFromCurrentPercent is not { } distance ||
                distance < value.MinimumDistancePercent,
            LiquidationDistanceEligibilityCondition value =>
                (result.Liquidation.DistanceFromCurrentPercent is { } distance &&
                 distance >= value.MinimumDistancePercent) != value.RequiredEligibility,
            PnlThresholdCondition value => !MeetsPnlThreshold(result.Pnl.PnlPercent, value),
            PnlAvailabilityCondition value =>
                result.Pnl.PnlPercent.HasValue != value.RequiredAvailability,
            HigherPriorityActionsCondition value =>
                !RecommendationActionPredicates.IsSafetyBlocked(assessment) &&
                value.RequiredActions.Any(action =>
                    RecommendationActionPredicates.IsActionRequired(assessment, policy, action)),
            AdditionalCapacityEligibilityCondition value =>
                (AdditionalPositionCapacityCalculator.Calculate(
                    assessment.Result.PortfolioRisk,
                    assessment.Result.CurrentPrice,
                    policy.AddAllowedLimits).MaximumPositionValue is > 0m) !=
                value.RequiredEligibility,
            DataQualityCondition value => result.DataQuality.Overall != value.RequiredQuality,
            SafetyStateCondition value => result.DataQuality.SafetyState != value.RequiredState,
            PortfolioRiskDecisionCondition value => assessment.PortfolioRiskDecision != value.RequiredDecision,
            LowVolumeCondition value => assessment.ReasonCodes.Contains(ReasonCode.LowVolume) != value.RequiredLowVolume,
            PolicyIdentityCondition value => policy.Identity != value.RequiredIdentity,
            OpposingLevelCondition value => !HasOpposingLevel(assessment, value.RequiredLevel),
            _ => throw new InvalidOperationException(
                $"Unsupported continuation condition type '{condition.GetType().Name}'.")
        };
    }

    private static bool CapacityChanged(
        AdditionalPositionCapacityResult current,
        AddAllowedCapacityCondition persisted) =>
        current.MaximumPositionValue != persisted.MaximumPositionValue ||
        current.MaximumQuantity != persisted.MaximumQuantity;

    private static bool CapacityReduced(
        AdditionalPositionCapacityResult current,
        AddAllowedCapacityCondition persisted) =>
        current.MaximumPositionValue is not > 0m ||
        current.MaximumPositionValue < persisted.MaximumPositionValue ||
        persisted.MaximumQuantity is { } persistedQuantity &&
        (current.MaximumQuantity is not { } currentQuantity || currentQuantity < persistedQuantity);

    private static bool MeetsPnlThreshold(
        decimal? pnl,
        PnlThresholdCondition condition) =>
        pnl is { } value && condition.Comparison switch
        {
            RecommendationPnlComparison.AtLeast => value >= condition.Threshold,
            RecommendationPnlComparison.AtMost => value <= condition.Threshold,
            _ => throw new ArgumentOutOfRangeException(
                nameof(condition),
                condition.Comparison,
                "PnL comparison must be defined.")
        };

    private static bool HasOpposingLevel(
        PositionAssessment assessment,
        RecommendationOpposingLevel level) =>
        assessment.ReasonCodes.Contains(level == RecommendationOpposingLevel.ResistanceNearby
            ? ReasonCode.ResistanceNearby
            : ReasonCode.SupportNearby);

    private static void AddTriggered(
        ICollection<RecommendationContinuationCondition> triggered,
        RecommendationContinuationCondition condition)
    {
        if (!triggered.Contains(condition))
            triggered.Add(condition);
    }
}
