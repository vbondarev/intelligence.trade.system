using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionMarketIdentityRepository(TradeSystemDbContext dbContext)
    : IPositionMarketIdentityStore
{
    public async Task<PositionMarketIdentity?> GetAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        if (positionId == default)
        {
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
        }

        var row = await dbContext.Positions
            .AsNoTracking()
            .Where(position => position.Id == positionId.Value)
            .Join(
                dbContext.ExchangeAccounts
                    .AsNoTracking()
                    .Where(account => account.UserId == userId.Value),
                position => position.ExchangeAccountId,
                account => account.Id,
                (position, account) => new
                {
                    position.Id,
                    position.ExchangeAccountId,
                    account.ExchangeId,
                    position.InstrumentId,
                    position.MarketCategory,
                })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new PositionMarketIdentity(
                PositionId.FromGuid(row.Id),
                ExchangeAccountId.FromGuid(row.ExchangeAccountId),
                row.ExchangeId,
                row.InstrumentId,
                row.MarketCategory);
    }
}
