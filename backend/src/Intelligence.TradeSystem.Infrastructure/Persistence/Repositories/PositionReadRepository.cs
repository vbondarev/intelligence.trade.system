using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
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

        var ownedAccountIds = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .Where(account => account.UserId == userId.Value)
            .Select(account => account.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var positions = dbContext.Positions
            .AsNoTracking()
            .Where(position => ownedAccountIds.Contains(position.ExchangeAccountId))
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

        var orderedPositions = positions
            .OrderByDescending(position => position.FirstDetectedAt)
            .ThenByDescending(position => position.Id);

        PositionReadRow[] rows;
        if (query.Cursor is not null &&
            query.ExchangeAccountId is null &&
            ownedAccountIds.Length > 1)
        {
            var accountRows = new List<PositionReadRow>(
                ownedAccountIds.Length * (query.PageSize + 1));
            foreach (var accountId in ownedAccountIds)
            {
                var accountPositions = dbContext.Positions
                    .AsNoTracking()
                    .Where(position =>
                        position.ExchangeAccountId == accountId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value))
                    .Where(position => query.TrackingStates.Contains(position.TrackingState));

                if (query.Symbol is { } accountSymbol)
                {
                    var normalizedSymbol = accountSymbol.ToLowerInvariant();
#pragma warning disable CA1304, CA1311, CA1862
                    accountPositions = accountPositions.Where(position =>
                        position.InstrumentId.ToLower() == normalizedSymbol);
#pragma warning restore CA1304, CA1311, CA1862
                }

                if (query.Side is { } accountSide)
                {
                    accountPositions = accountPositions.Where(position =>
                        position.PositionSide == accountSide);
                }

                if (query.Cursor is { } accountCursor)
                {
                    accountPositions = accountPositions.Where(position =>
                        position.FirstDetectedAt < accountCursor.FirstDetectedAt ||
                        (position.FirstDetectedAt == accountCursor.FirstDetectedAt &&
                         position.Id.CompareTo(accountCursor.PositionId.Value) < 0));
                }

                accountRows.AddRange(
                    await SelectListRows(accountPositions
                            .OrderByDescending(position => position.FirstDetectedAt)
                            .ThenByDescending(position => position.Id))
                        .Take(query.PageSize + 1)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false));
            }

            rows = accountRows
                .OrderByDescending(row => row.FirstDetectedAt)
                .ThenByDescending(row => row.Id)
                .Take(query.PageSize + 1)
                .ToArray();
        }
        else
        {
            rows = await SelectListRows(orderedPositions)
                .Take(query.PageSize + 1)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

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

    private static IQueryable<PositionReadRow> SelectListRows(
        IQueryable<PositionEntity> positions) =>
        positions.Select(position => new PositionReadRow(
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
            position.ClosedAt));

    private sealed record PositionReadRow(
        Guid Id,
        Guid ExchangeAccountId,
        string InstrumentId,
        PositionSide PositionSide,
        PositionTrackingState TrackingState,
        decimal Size,
        decimal? AverageEntryPrice,
        decimal? MarkPrice,
        decimal? PositionValue,
        decimal? UnrealizedPnl,
        decimal? Leverage,
        decimal? LiquidationPrice,
        DateTimeOffset FirstDetectedAt,
        DateTimeOffset LastObservedAt,
        DateTimeOffset? ClosedAt);
}
