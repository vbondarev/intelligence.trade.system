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

        PositionReadRow[] rows;
        if (query.Cursor is not null && query.ExchangeAccountId is null)
        {
            var ownedAccountIds = await FindOwnedAccountIdsAsync(
                    userId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (ownedAccountIds.Length == 0)
            {
                return EmptyPage();
            }

            var accountRows = new List<PositionReadRow>(
                ownedAccountIds.Length * (query.PageSize + 1));
            if (ownedAccountIds.Length > 1)
            {
                foreach (var accountId in ownedAccountIds)
                {
                    accountRows.AddRange(
                        await SelectListRows(
                                ApplyListFilters(
                                        CreatePositionQuery(userId, query, accountId),
                                        query)
                                    .OrderByDescending(position => position.FirstDetectedAt)
                                    .ThenByDescending(position => position.Id))
                            .Take(query.PageSize + 1)
                            .ToArrayAsync(cancellationToken)
                            .ConfigureAwait(false));
                }
            }
            else
            {
                accountRows.AddRange(
                    await SelectListRows(
                            ApplyListFilters(
                                    CreatePositionQuery(userId, query, ownedAccountIds[0]),
                                    query)
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
            rows = await SelectListRows(
                    ApplyListFilters(
                            CreatePositionQuery(
                                userId,
                                query,
                                query.ExchangeAccountId?.Value),
                            query)
                        .OrderByDescending(position => position.FirstDetectedAt)
                        .ThenByDescending(position => position.Id))
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

    private IQueryable<PositionEntity> CreatePositionQuery(
        UserId userId,
        PositionReadQuery query,
        Guid? exchangeAccountId) =>
        dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .Where(position => exchangeAccountId == null ||
                position.ExchangeAccountId == exchangeAccountId.Value);

    private static IQueryable<PositionEntity> ApplyListFilters(
        IQueryable<PositionEntity> positions,
        PositionReadQuery query)
    {
        positions = positions.Where(position =>
            query.TrackingStates.Contains(position.TrackingState));

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
            var beforeCursor = positions.Where(position =>
                position.FirstDetectedAt < cursor.FirstDetectedAt);
            var atCursorTimestamp = positions.Where(position =>
                position.FirstDetectedAt == cursor.FirstDetectedAt &&
                position.Id.CompareTo(cursor.PositionId.Value) < 0);
            positions = beforeCursor.Concat(atCursorTimestamp);
        }

        return positions;
    }

    private Task<Guid[]> FindOwnedAccountIdsAsync(
        UserId userId,
        CancellationToken cancellationToken) =>
        dbContext.ExchangeAccounts
            .AsNoTracking()
            .Where(account => account.UserId == userId.Value)
            .Select(account => account.Id)
            .ToArrayAsync(cancellationToken);

    private static PositionReadPage EmptyPage() =>
        new([], null, false);

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
