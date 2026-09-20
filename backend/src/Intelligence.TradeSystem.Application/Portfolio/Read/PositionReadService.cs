using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public sealed class PositionReadService(IPositionReadStore store)
{
    public Task<PositionReadPage> ListAsync(
        UserId userId,
        PositionReadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return store.ListAsync(userId, query, cancellationToken);
    }

    public Task<PositionReadDetail?> GetByIdAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default) =>
        store.GetByIdAsync(userId, positionId, cancellationToken);
}
