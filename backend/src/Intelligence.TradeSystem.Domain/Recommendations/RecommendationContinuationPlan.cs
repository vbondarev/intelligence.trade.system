using System.Collections.ObjectModel;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Неизменяемый контекст применимости уже созданной рекомендации.
/// </summary>
public sealed record RecommendationContinuationPlan
{
    public RecommendationContinuationPlan(
        IEnumerable<RecommendationContinuationCondition> invalidationConditions,
        IEnumerable<RecommendationContinuationCondition> reevaluationConditions,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        DateTimeOffset nextEvaluationAt)
    {
        ArgumentNullException.ThrowIfNull(invalidationConditions);
        ArgumentNullException.ThrowIfNull(reevaluationConditions);
        if (createdAt == default)
            throw new ArgumentException("CreatedAt must be initialized.", nameof(createdAt));
        if (validUntil == default)
            throw new ArgumentException("ValidUntil must be initialized.", nameof(validUntil));
        if (nextEvaluationAt == default)
            throw new ArgumentException("NextEvaluationAt must be initialized.", nameof(nextEvaluationAt));
        if (createdAt >= validUntil)
            throw new ArgumentException("CreatedAt must be before ValidUntil.", nameof(validUntil));
        if (createdAt >= nextEvaluationAt)
            throw new ArgumentException("CreatedAt must be before NextEvaluationAt.", nameof(nextEvaluationAt));
        if (nextEvaluationAt > validUntil)
            throw new ArgumentException("NextEvaluationAt cannot exceed ValidUntil.", nameof(nextEvaluationAt));

        var invalidation = invalidationConditions.ToArray();
        var reevaluation = reevaluationConditions.ToArray();
        ValidateConditions(invalidation, nameof(invalidationConditions));
        ValidateConditions(reevaluation, nameof(reevaluationConditions));
        if (invalidation.Length == 0)
            throw new ArgumentException("At least one invalidation condition is required.", nameof(invalidationConditions));
        if (reevaluation.Length == 0)
            throw new ArgumentException("At least one reevaluation condition is required.", nameof(reevaluationConditions));
        ValidatePolicyIdentityConditions(invalidation, reevaluation);
        var expiryConditions = invalidation.OfType<RecommendationExpiryCondition>().ToArray();
        if (expiryConditions.Length != 1 || expiryConditions[0].ValidUntil != validUntil)
            throw new ArgumentException(
                "Exactly one expiry condition matching ValidUntil is required.",
                nameof(invalidationConditions));
        if (reevaluation.OfType<RecommendationExpiryCondition>().Any())
            throw new ArgumentException(
                "Expiry conditions must belong to the invalidation list.",
                nameof(reevaluationConditions));

        InvalidationConditions = new ReadOnlyCollection<RecommendationContinuationCondition>(invalidation);
        ReevaluationConditions = new ReadOnlyCollection<RecommendationContinuationCondition>(reevaluation);
        CreatedAt = createdAt;
        ValidUntil = validUntil;
        NextEvaluationAt = nextEvaluationAt;
    }

    public IReadOnlyList<RecommendationContinuationCondition> InvalidationConditions { get; }
    public IReadOnlyList<RecommendationContinuationCondition> ReevaluationConditions { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public DateTimeOffset NextEvaluationAt { get; }

    private static void ValidateConditions(
        IReadOnlyList<RecommendationContinuationCondition> conditions,
        string parameterName)
    {
        var keys = new HashSet<(RecommendationContinuationConditionScope Scope, RecommendationContinuationConditionKind Kind)>();
        foreach (var condition in conditions)
        {
            if (condition is null)
                throw new ArgumentException("Condition entries cannot be null.", parameterName);
            if (!Enum.IsDefined(condition.Scope) || !Enum.IsDefined(condition.Kind))
                throw new ArgumentException("Condition scope and kind must be defined.", parameterName);

            var expectedKind = condition switch
            {
                TrendAlignmentCondition => RecommendationContinuationConditionKind.TrendAlignment,
                MomentumReliabilityCondition => RecommendationContinuationConditionKind.MomentumReliability,
                MomentumStateCondition => RecommendationContinuationConditionKind.MomentumState,
                MomentumAvailabilityCondition => RecommendationContinuationConditionKind.MomentumAvailability,
                MomentumExhaustionCondition => RecommendationContinuationConditionKind.MomentumExhaustion,
                StopStateCondition => RecommendationContinuationConditionKind.StopState,
                StopAvailabilityCondition => RecommendationContinuationConditionKind.StopAvailability,
                StopRelativePositionCondition => RecommendationContinuationConditionKind.StopRelativePosition,
                ProfitProtectionCondition => RecommendationContinuationConditionKind.ProfitProtection,
                LiquidationStateCondition => RecommendationContinuationConditionKind.LiquidationState,
                LiquidationDistanceCondition => RecommendationContinuationConditionKind.LiquidationDistance,
                PnlThresholdCondition => RecommendationContinuationConditionKind.PnlThreshold,
                PnlAvailabilityCondition => RecommendationContinuationConditionKind.PnlAvailability,
                HigherPriorityActionsCondition => RecommendationContinuationConditionKind.HigherPriorityActions,
                DataQualityCondition => RecommendationContinuationConditionKind.DataQuality,
                SafetyStateCondition => RecommendationContinuationConditionKind.SafetyState,
                PortfolioRiskDecisionCondition => RecommendationContinuationConditionKind.PortfolioRiskDecision,
                LowVolumeCondition => RecommendationContinuationConditionKind.LowVolume,
                PolicyIdentityCondition => RecommendationContinuationConditionKind.PolicyIdentity,
                AddAllowedCapacityCondition => RecommendationContinuationConditionKind.AddAllowedCapacity,
                OpposingLevelCondition => RecommendationContinuationConditionKind.OpposingLevel,
                RecommendationExpiryCondition => RecommendationContinuationConditionKind.RecommendationExpiry,
                ContinuationContextUnavailableCondition =>
                    RecommendationContinuationConditionKind.ContinuationContextUnavailable,
                _ => throw new ArgumentException(
                    $"Unsupported continuation condition type '{condition.GetType().Name}'.",
                    parameterName)
            };

            if (condition.Kind != expectedKind)
                throw new ArgumentException(
                    $"Condition kind {condition.Kind} does not match runtime type {condition.GetType().Name}.",
                    parameterName);
            if (condition is PolicyIdentityCondition &&
                condition.Scope != RecommendationContinuationConditionScope.Recommendation)
                throw new ArgumentException(
                    "Policy identity conditions must use Recommendation scope.",
                    parameterName);
            if (condition is RecommendationExpiryCondition &&
                condition.Scope != RecommendationContinuationConditionScope.Recommendation)
                throw new ArgumentException(
                    "Expiry conditions must use Recommendation scope.",
                    parameterName);
            if (condition is ContinuationContextUnavailableCondition)
                throw new ArgumentException(
                    "ContinuationContextUnavailable is reserved for legacy recommendations.",
                    parameterName);
            if (!keys.Add((condition.Scope, condition.Kind)))
                throw new ArgumentException(
                    $"Duplicate semantic condition {condition.Scope}/{condition.Kind}.",
                    parameterName);
        }
    }

    private static void ValidatePolicyIdentityConditions(
        IReadOnlyList<RecommendationContinuationCondition> invalidation,
        IReadOnlyList<RecommendationContinuationCondition> reevaluation)
    {
        var policyConditions = invalidation
            .OfType<PolicyIdentityCondition>()
            .ToArray();
        if (policyConditions.Length != 1)
            throw new ArgumentException(
                "Exactly one policy identity condition is required in invalidation conditions.",
                nameof(invalidation));
        if (reevaluation.OfType<PolicyIdentityCondition>().Any())
            throw new ArgumentException(
                "Policy identity conditions must not be reevaluation conditions.",
                nameof(reevaluation));
    }
}
