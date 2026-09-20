using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionReadRepository(TradeSystemDbContext dbContext) : IPositionReadStore
{
    public async Task<PositionReadPage> ListAsync(
        UserId userId,
        PositionReadQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        ArgumentNullException.ThrowIfNull(query);

        var positions = dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .Where(position => query.TrackingStates.Contains(position.TrackingState));

        if (query.ExchangeAccountId is { } exchangeAccountId)
        {
            positions = positions.Where(position =>
                position.ExchangeAccountId == exchangeAccountId.Value);
        }

        if (query.Symbol is { } symbol)
        {
            var normalizedSymbol = symbol.ToLowerInvariant();
#pragma warning disable CA1304, CA1311, CA1862
            positions = positions.Where(position =>
                position.InstrumentId.ToLower() == normalizedSymbol);
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (query.Side is { } side)
        {
            positions = positions.Where(position => position.PositionSide == side);
        }

        if (query.Cursor is { } cursor)
        {
            positions = positions.Where(position =>
                position.FirstDetectedAt < cursor.FirstDetectedAt ||
                (position.FirstDetectedAt == cursor.FirstDetectedAt &&
                 position.Id.CompareTo(cursor.PositionId.Value) < 0));
        }

        var rows = await positions
            .OrderByDescending(position => position.FirstDetectedAt)
            .ThenByDescending(position => position.Id)
            .Select(position => new
            {
                position.Id,
                position.ExchangeAccountId,
                position.InstrumentId,
                position.PositionSide,
                position.TrackingState,
                position.Size,
                position.AverageEntryPrice,
                position.MarkPrice,
                position.PositionValue,
                position.UnrealizedPnl,
                position.Leverage,
                position.LiquidationPrice,
                position.FirstDetectedAt,
                position.LastObservedAt,
                position.ClosedAt,
            })
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Length > query.PageSize;
        var pageRows = hasMore ? rows[..query.PageSize] : rows;
        var items = pageRows
            .Select(row => new PositionReadListItem(
                PositionId.FromGuid(row.Id),
                ExchangeAccountId.FromGuid(row.ExchangeAccountId),
                row.InstrumentId,
                row.PositionSide,
                row.TrackingState,
                row.Size,
                row.AverageEntryPrice,
                row.MarkPrice,
                row.PositionValue,
                row.UnrealizedPnl,
                row.Leverage,
                row.LiquidationPrice,
                row.FirstDetectedAt,
                row.LastObservedAt,
                row.ClosedAt))
            .ToArray();

        PositionReadCursor? nextCursor = hasMore && items.Length > 0
            ? new PositionReadCursor(items[^1].FirstDetectedAt, items[^1].Id)
            : null;

        return new PositionReadPage(items, nextCursor, hasMore);
    }

    public async Task<PositionReadDetail?> GetByIdAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var row = await dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                position.Id == positionId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .Select(position => new
            {
                position.Id,
                position.ExchangeAccountId,
                position.InstrumentId,
                position.PositionSide,
                position.TrackingState,
                position.Size,
                position.AverageEntryPrice,
                position.MarkPrice,
                position.PositionValue,
                position.UnrealizedPnl,
                position.Leverage,
                position.LiquidationPrice,
                position.FirstDetectedAt,
                position.LastObservedAt,
                position.ClosedAt,
                position.MarketCategory,
                position.BreakEvenPrice,
                position.TakeProfit,
                position.StopLoss,
                position.TrailingStop,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var listItem = new PositionReadListItem(
            PositionId.FromGuid(row.Id),
            ExchangeAccountId.FromGuid(row.ExchangeAccountId),
            row.InstrumentId,
            row.PositionSide,
            row.TrackingState,
            row.Size,
            row.AverageEntryPrice,
            row.MarkPrice,
            row.PositionValue,
            row.UnrealizedPnl,
            row.Leverage,
            row.LiquidationPrice,
            row.FirstDetectedAt,
            row.LastObservedAt,
            row.ClosedAt);

        return new PositionReadDetail(
            listItem,
            row.MarketCategory,
            row.BreakEvenPrice,
            row.TakeProfit,
            row.StopLoss,
            row.TrailingStop);
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }
}
