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
        _ = conditions;
        _ = parameterName;
    }
}

