using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public interface IRecommendationPublicationTransaction
{
    Task PublishInitialAsync(
        UserId userId,
        Recommendation successor,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        UserId userId,
        Recommendation current,
        Recommendation successor,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default);

    Task SavePendingAsync(
        UserId userId,
        PositionId positionId,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateSnapshot state,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default);

    Task ConfirmKeepExistingAsync(
        UserId userId,
        PositionId positionId,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default);
}
