using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public interface IPositionReadStore
{
    Task<IReadOnlyList<PositionMarketValuationInput>> GetMarketValuationInputsByAccountAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task<PositionReadPage> ListAsync(
        UserId userId,
        PositionReadQuery query,
        CancellationToken cancellationToken = default);

    Task<PositionReadDetail?> GetByIdAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default);
}
