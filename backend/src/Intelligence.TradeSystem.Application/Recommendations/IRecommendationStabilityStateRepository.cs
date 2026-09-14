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
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        UserId userId,
        PositionId positionId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);
}
