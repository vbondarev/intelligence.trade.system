using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PortfolioReadRepository(
    TradeSystemDbContext dbContext,
    TimeProvider timeProvider) : IPortfolioReadStore
{
    public async Task<PortfolioReadResult> GetLatestAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var accountExists = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == exchangeAccountId.Value &&
                           account.UserId == userId.Value,
                cancellationToken)
            .ConfigureAwait(false);

        if (!accountExists)
        {
            return new PortfolioReadResult(false, null);
        }

        var entity = await dbContext.PortfolioStates
            .AsNoTracking()
            .Where(state =>
                state.ExchangeAccountId == exchangeAccountId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == state.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderByDescending(state => state.CalculatedAt)
            .ThenByDescending(state => state.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return new PortfolioReadResult(true, null);
        }

        var positions = await dbContext.PortfolioPositionStates
            .AsNoTracking()
            .Where(position => position.PortfolioStateId == entity.Id)
            .OrderBy(position => position.Sequence)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = PortfolioStateMapper.ToDomain(entity, positions);
        return new PortfolioReadResult(
            true,
            PortfolioReadProjection.Project(state, timeProvider.GetUtcNow()));
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }
}
