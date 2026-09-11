using Intelligence.TradeSystem.Domain.Assessments;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>Полный детерминированный результат оценки RecommendationPolicy.</summary>
public sealed record RecommendationPolicyEvaluation
{
    internal RecommendationPolicyEvaluation(
        PolicyConfigurationIdentity policyIdentity,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
    {
        if (string.IsNullOrWhiteSpace(policyIdentity.Version) ||
            string.IsNullOrWhiteSpace(policyIdentity.Hash))
            throw new ArgumentException("Policy identity must contain version and hash.", nameof(policyIdentity));
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(addDecision);
        if (validUntil <= createdAt)
            throw new ArgumentException("Evaluation validity period must be positive.", nameof(validUntil));
        if (addDecision.Decision == Decisions.AddDecision.NotEvaluated)
            throw new ArgumentException("New policy evaluations cannot use NotEvaluated.", nameof(addDecision));

        PolicyIdentity = policyIdentity;
        Action = action;
        AddDecision = addDecision;
        CreatedAt = createdAt;
        ValidUntil = validUntil;
    }

    public PolicyConfigurationIdentity PolicyIdentity { get; }
    public RecommendedActionDecision Action { get; }
    public AddDecisionResult AddDecision { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
}
