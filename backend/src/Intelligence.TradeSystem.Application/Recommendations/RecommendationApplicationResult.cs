using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public enum RecommendationApplicationResultKind
{
    KeptExisting,
    PendingConfirmation,
    Published
}

public sealed record RecommendationApplicationResult(
    RecommendationApplicationResultKind Kind,
    Recommendation? CurrentRecommendation,
    RecommendationStabilityReason StabilityReason)
{
    public Recommendation? Recommendation => CurrentRecommendation;
}
