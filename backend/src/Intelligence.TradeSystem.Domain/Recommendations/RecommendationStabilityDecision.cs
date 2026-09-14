namespace Intelligence.TradeSystem.Domain.Recommendations;

public enum RecommendationStabilityDecisionKind
{
    KeepExisting,
    PendingConfirmation,
    PublishCandidate
}

public enum RecommendationStabilityReason
{
    Duplicate,
    WithinCooldown,
    AwaitingConfirmation,
    CandidateChanged,
    MaterialChange,
    RiskReduction,
    SafetyEscalation,
    AddPermissionRevoked,
    AddPermissionGranted,
    PriorityEscalation,
    PolicyChanged,
    CurrentExpired,
    CurrentInactive,
    NoCurrentRecommendation
}

/// <summary>Типизированная классификация результата стабилизации.</summary>
public sealed record RecommendationStabilityDecision
{
    public RecommendationStabilityDecision(
        RecommendationStabilityDecisionKind kind,
        RecommendationStabilityReason reason)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Decision kind must be defined.");
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Decision reason must be defined.");

        Kind = kind;
        Reason = reason;
    }

    public RecommendationStabilityDecisionKind Kind { get; }
    public RecommendationStabilityDecisionKind DecisionKind => Kind;
    public RecommendationStabilityReason Reason { get; }
}

/// <summary>Детерминированный результат оценки stability policy вместе с состоянием для сохранения.</summary>
public sealed record RecommendationStabilityEvaluation
{
    public RecommendationStabilityEvaluation(
        RecommendationStabilityDecision decision,
        RecommendationStabilityState? nextState)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Kind == RecommendationStabilityDecisionKind.PendingConfirmation && nextState is null)
            throw new ArgumentException(
                "Pending confirmation must return the next stability state.",
                nameof(nextState));
        if (decision.Kind != RecommendationStabilityDecisionKind.PendingConfirmation && nextState is not null)
            throw new ArgumentException(
                "Only pending confirmation may return a next stability state.",
                nameof(nextState));

        Decision = decision;
        NextState = nextState;
    }

    public RecommendationStabilityDecision Decision { get; }
    public RecommendationStabilityDecisionKind Kind => Decision.Kind;
    public RecommendationStabilityReason Reason => Decision.Reason;
    public RecommendationStabilityState? NextState { get; }
}
