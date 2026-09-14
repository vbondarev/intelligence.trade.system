using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public interface IRecommendationPublicationTransaction
{
    Task PublishInitialAsync(
        UserId userId,
        Recommendation successor,
        ConcurrencyVersion? expectedPendingVersion,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        UserId userId,
        Recommendation current,
        ConcurrencyVersion expectedCurrentVersion,
        Recommendation successor,
        ConcurrencyVersion? expectedPendingVersion,
        CancellationToken cancellationToken = default);
}
