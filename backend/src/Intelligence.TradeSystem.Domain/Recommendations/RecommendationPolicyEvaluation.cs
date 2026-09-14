using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>Полный детерминированный результат оценки RecommendationPolicy.</summary>
public sealed record RecommendationPolicyEvaluation
{
    internal RecommendationPolicyEvaluation(
        PolicyConfigurationIdentity policyIdentity,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        RecommendationContinuationPlan continuationPlan,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        IEnumerable<ReasonCode> inheritedReasonCodes)
    {
        if (string.IsNullOrWhiteSpace(policyIdentity.Version) ||
            string.IsNullOrWhiteSpace(policyIdentity.Hash))
            throw new ArgumentException("Policy identity must contain version and hash.", nameof(policyIdentity));
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(addDecision);
        ArgumentNullException.ThrowIfNull(continuationPlan);
        ArgumentNullException.ThrowIfNull(inheritedReasonCodes);
        if (validUntil <= createdAt)
            throw new ArgumentException("Evaluation validity period must be positive.", nameof(validUntil));
        if (addDecision.Decision == Decisions.AddDecision.NotEvaluated)
            throw new ArgumentException("New policy evaluations cannot use NotEvaluated.", nameof(addDecision));
        if (continuationPlan.CreatedAt != createdAt || continuationPlan.ValidUntil != validUntil)
            throw new ArgumentException(
                "Continuation plan timestamps must match evaluation timestamps.",
                nameof(continuationPlan));

        var inherited = inheritedReasonCodes.ToArray();
        if (inherited.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(
                nameof(inheritedReasonCodes),
                "Inherited reason code must be defined.");
        if (inherited.Any(reason => !ReasonCodeClassification.IsPortfolioRiskReason(reason)))
            throw new ArgumentException(
                "Inherited reasons must be portfolio-risk reason codes.",
                nameof(inheritedReasonCodes));
        if (inherited.Distinct().Count() != inherited.Length)
            throw new ArgumentException(
                "Inherited reason codes cannot contain duplicates.",
                nameof(inheritedReasonCodes));

        PolicyIdentity = policyIdentity;
        Action = action;
        AddDecision = addDecision;
        ContinuationPlan = continuationPlan;
        InheritedReasonCodes = new ReadOnlyCollection<ReasonCode>(
            inherited.OrderBy(reason => (int)reason).ToArray());
        CreatedAt = createdAt;
        ValidUntil = validUntil;
    }

    public PolicyConfigurationIdentity PolicyIdentity { get; }
    public RecommendedActionDecision Action { get; }
    public AddDecisionResult AddDecision { get; }
    public RecommendationContinuationPlan ContinuationPlan { get; }
    public IReadOnlyList<ReasonCode> InheritedReasonCodes { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
}
