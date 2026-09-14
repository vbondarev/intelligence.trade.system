using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public sealed record RecommendationStabilityStateSnapshot
{
    public RecommendationStabilityStateSnapshot(
        RecommendationId baselineRecommendationId,
        RecommendationStabilityState state)
        : this(Guid.NewGuid(), baselineRecommendationId, state)
    {
    }

    public RecommendationStabilityStateSnapshot(
        Guid stateId,
        RecommendationId baselineRecommendationId,
        RecommendationStabilityState state)
    {
        if (stateId == Guid.Empty)
            throw new ArgumentException("StateId must be initialized.", nameof(stateId));
        if (baselineRecommendationId == default)
            throw new ArgumentException(
                "Baseline recommendation id must be initialized.",
                nameof(baselineRecommendationId));
        ArgumentNullException.ThrowIfNull(state);

        StateId = stateId;
        BaselineRecommendationId = baselineRecommendationId;
        State = state;
    }

    public Guid StateId { get; }
    public RecommendationId BaselineRecommendationId { get; }
    public RecommendationStabilityState State { get; }
}
