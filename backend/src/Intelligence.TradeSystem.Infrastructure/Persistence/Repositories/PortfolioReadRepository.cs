using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PortfolioReadRepository(TradeSystemDbContext dbContext) : IPortfolioReadStore
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

        var row = await dbContext.PortfolioStates
            .AsNoTracking()
            .Where(state =>
                state.ExchangeAccountId == exchangeAccountId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == state.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderByDescending(state => state.CalculatedAt)
            .ThenByDescending(state => state.Id)
            .Select(state => new
            {
                state.ExchangeAccountId,
                state.CalculatedAt,
                state.TotalEquity,
                state.AvailableCapital,
                state.TotalWalletBalance,
                state.CapitalObservedAt,
                state.GrossExposure,
                state.LongExposure,
                state.ShortExposure,
                state.NetExposure,
                state.TotalUnrealizedPnl,
                state.UsedCapital,
                state.FreeCapital,
                state.FreeCapitalPercent,
                state.GrossExposureToEquityPercent,
                state.LargestPositionConcentrationPercent,
                state.LargestPositionId,
                state.PositionsFullyReconciled,
                state.IsComplete,
                state.IsFresh,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return new PortfolioReadResult(true, null);
        }

        return new PortfolioReadResult(
            true,
            new PortfolioReadSummary(
                ExchangeAccountId.FromGuid(row.ExchangeAccountId),
                row.CalculatedAt,
                row.TotalEquity,
                row.AvailableCapital,
                row.TotalWalletBalance,
                row.CapitalObservedAt,
                row.GrossExposure,
                row.LongExposure,
                row.ShortExposure,
                row.NetExposure,
                row.TotalUnrealizedPnl,
                row.UsedCapital,
                row.FreeCapital,
                row.FreeCapitalPercent,
                row.GrossExposureToEquityPercent,
                row.LargestPositionConcentrationPercent,
                row.LargestPositionId is { } largestPositionId
                    ? PositionId.FromGuid(largestPositionId)
                    : null,
                row.PositionsFullyReconciled,
                row.IsComplete,
                row.IsFresh));
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }
}
