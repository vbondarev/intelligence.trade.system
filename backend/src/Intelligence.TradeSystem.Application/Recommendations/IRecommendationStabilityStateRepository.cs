using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Recommendations;

public interface IRecommendationStabilityStateRepository
{
    Task<Versioned<RecommendationStabilityStateSnapshot>?> GetAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default);

    Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        PositionId positionId,
        RecommendationStabilityStateSnapshot state,
        RecommendationStabilityStateExpectation expectedState,
        CancellationToken cancellationToken = default);

    Task DeleteExpectedAsync(
        UserId userId,
        PositionId positionId,
        RecommendationStabilityStateExpectation expectedState,
        CancellationToken cancellationToken = default);
}
