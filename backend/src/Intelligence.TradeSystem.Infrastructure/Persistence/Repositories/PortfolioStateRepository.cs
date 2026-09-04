using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PortfolioStateRepository(TradeSystemDbContext dbContext) : IPortfolioStateRepository
{
    public async Task<PortfolioState?> GetLatestAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PortfolioStates
            .AsNoTracking()
            .Where(state => state.ExchangeAccountId == exchangeAccountId.Value)
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
        PortfolioState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        dbContext.PortfolioStates.Add(PortfolioStateMapper.ToEntity(state));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
