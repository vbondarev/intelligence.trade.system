using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PortfolioStateRepository(TradeSystemDbContext dbContext) : IPortfolioStateRepository
{
    public async Task<PortfolioState?> GetLatestAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        var entity = await dbContext.PortfolioStates
            .AsNoTracking()
            .Where(state =>
                state.ExchangeAccountId == exchangeAccountId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == state.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderByDescending(state => state.CalculatedAt)
            .ThenByDescending(state => state.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null) return null;

        var positions = await dbContext.PortfolioPositionStates
            .AsNoTracking()
            .Where(position => position.PortfolioStateId == entity.Id)
            .OrderBy(position => position.Sequence)
            .ToArrayAsync(cancellationToken);

        return PortfolioStateMapper.ToDomain(entity, positions);
    }

    public async Task SaveAsync(
        UserId userId,
        PortfolioState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureUserId(userId);
        if (!await dbContext.ExchangeAccounts.AnyAsync(
                account =>
                    account.Id == state.ExchangeAccountId.Value &&
                    account.UserId == userId.Value,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "A portfolio state can only be saved within its owning user scope.");
        }

        dbContext.PortfolioStates.Add(PortfolioStateMapper.ToEntity(state));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
    }
}
