using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Market.Positions;

public interface IPositionMarketIdentityStore
{
    Task<PositionMarketIdentity?> GetAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default);
}
