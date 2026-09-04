using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<Recommendation?> GetByIdAsync(RecommendationId id, CancellationToken cancellationToken = default);

    Task SaveAsync(Recommendation recommendation, CancellationToken cancellationToken = default);
}
