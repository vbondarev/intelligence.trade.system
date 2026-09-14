using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public sealed record RecommendationStabilityStateSnapshot
{
    public RecommendationStabilityStateSnapshot(
        RecommendationId baselineRecommendationId,
        RecommendationStabilityState state)
    {
        if (baselineRecommendationId == default)
            throw new ArgumentException(
                "Baseline recommendation id must be initialized.",
                nameof(baselineRecommendationId));
        ArgumentNullException.ThrowIfNull(state);

        BaselineRecommendationId = baselineRecommendationId;
        State = state;
    }

    public RecommendationId BaselineRecommendationId { get; }
    public RecommendationStabilityState State { get; }
}
