using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Recommendations;

public abstract record RecommendationCurrentExpectation
{
    private RecommendationCurrentExpectation()
    {
    }

    public sealed record Absent : RecommendationCurrentExpectation;

    public sealed record Present : RecommendationCurrentExpectation
    {
        public Present(RecommendationId recommendationId, ConcurrencyVersion version)
        {
            if (recommendationId == default)
                throw new ArgumentException(
                    "Recommendation id must be initialized.",
                    nameof(recommendationId));

            RecommendationId = recommendationId;
            Version = version;
        }

        public RecommendationId RecommendationId { get; }
        public ConcurrencyVersion Version { get; }
    }
}

public abstract record RecommendationStabilityStateExpectation
{
    private RecommendationStabilityStateExpectation()
    {
    }

    public sealed record Absent : RecommendationStabilityStateExpectation;

    public sealed record Present : RecommendationStabilityStateExpectation
    {
        public Present(
            Guid stateId,
            RecommendationId baselineRecommendationId,
            ConcurrencyVersion version)
        {
            if (stateId == Guid.Empty)
                throw new ArgumentException("StateId must be initialized.", nameof(stateId));
            if (baselineRecommendationId == default)
                throw new ArgumentException(
                    "Baseline recommendation id must be initialized.",
                    nameof(baselineRecommendationId));

            StateId = stateId;
            BaselineRecommendationId = baselineRecommendationId;
            Version = version;
        }

        public Guid StateId { get; }
        public RecommendationId BaselineRecommendationId { get; }
        public ConcurrencyVersion Version { get; }
    }
}
