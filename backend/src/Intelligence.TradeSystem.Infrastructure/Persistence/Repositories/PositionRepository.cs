using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionRepository(TradeSystemDbContext dbContext) : IPositionRepository
{
    public async Task<IReadOnlyCollection<Versioned<Position>>> GetByExchangeAccountAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var entities = await dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                position.ExchangeAccountId == exchangeAccountId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderBy(position => position.Id)
            .ToArrayAsync(cancellationToken);

        if (entities.Length == 0)
        {
            return [];
        }

        var positionIds = entities.Select(position => position.Id).ToArray();
        var changes = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change => positionIds.Contains(change.PositionId))
            .OrderBy(change => change.PositionId)
            .ThenBy(change => change.Sequence)
            .ToArrayAsync(cancellationToken);
        var changesByPosition = changes
            .GroupBy(change => change.PositionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<PositionChangeEntity>)group.ToArray());

        return entities
            .Select(entity => new Versioned<Position>(
                PositionMapper.ToDomain(
                    entity,
                    changesByPosition.TryGetValue(entity.Id, out var positionChanges)
                        ? positionChanges
                        : []),
                new ConcurrencyVersion(entity.Version)))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<PositionVersionWatermark>> GetCurrentVersionWatermarksAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var rows = await dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                position.ExchangeAccountId == exchangeAccountId.Value &&
                position.TrackingState != PositionTrackingState.Closed &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderBy(position => position.Id)
            .Select(position => new { position.Id, position.Version })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => new PositionVersionWatermark(
                PositionId.FromGuid(row.Id),
                new ConcurrencyVersion(row.Version)))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<PositionPortfolioProjection>> GetCurrentPortfolioPositionsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var rows = await dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                position.ExchangeAccountId == exchangeAccountId.Value &&
                position.TrackingState != PositionTrackingState.Closed &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderBy(position => position.Id)
            .Select(position => new
            {
                position.Id,
                position.Version,
                position.ExchangeAccountId,
                position.InstrumentId,
                position.PositionSide,
                position.PositionIdx,
                position.MarketCategory,
                position.SettlementAsset,
                position.TrackingState,
                position.Size,
                position.PositionValue,
                position.UnrealizedPnl,
                position.AverageEntryPrice,
                position.MarkPrice,
                position.LiquidationPrice,
                position.Leverage,
                position.LastObservedAt,
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                var key = ExchangePositionKey.Create(
                    ExchangeAccountId.FromGuid(row.ExchangeAccountId),
                    InstrumentId.From(row.InstrumentId),
                    row.PositionSide,
                    row.PositionIdx);
                var state = new PortfolioPositionState(
                    PositionId.FromGuid(row.Id),
                    key,
                    row.MarketCategory,
                    SettlementAsset.From(row.SettlementAsset),
                    row.PositionSide,
                    row.TrackingState,
                    row.Size,
                    row.PositionValue,
                    row.UnrealizedPnl,
                    row.AverageEntryPrice,
                    row.MarkPrice,
                    row.LiquidationPrice,
                    row.Leverage,
                    PersistenceDateTime.ToUtc(row.LastObservedAt));
                return new PositionPortfolioProjection(
                    state.PositionId,
                    new ConcurrencyVersion(row.Version),
                    state);
            })
            .ToArray();
    }

    public async Task<IReadOnlyCollection<Versioned<Position>>> GetCurrentWithHistoryAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var entities = await dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                position.ExchangeAccountId == exchangeAccountId.Value &&
                position.TrackingState != PositionTrackingState.Closed &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .OrderBy(position => position.Id)
            .ToArrayAsync(cancellationToken);

        if (entities.Length == 0)
        {
            return [];
        }

        var positionIds = entities.Select(position => position.Id).ToArray();
        var changes = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change => positionIds.Contains(change.PositionId))
            .OrderBy(change => change.PositionId)
            .ThenBy(change => change.Sequence)
            .ToArrayAsync(cancellationToken);
        var changesByPosition = changes
            .GroupBy(change => change.PositionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<PositionChangeEntity>)group.ToArray());

        return entities
            .Select(entity => new Versioned<Position>(
                PositionMapper.ToDomain(
                    entity,
                    changesByPosition.TryGetValue(entity.Id, out var positionChanges)
                        ? positionChanges
                        : []),
                new ConcurrencyVersion(entity.Version)))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<LatestClosedPositionSlot>> GetLatestClosedSlotsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        MarketCategory marketCategory,
        IReadOnlyCollection<PositionSlotKey> slots,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        ArgumentNullException.ThrowIfNull(slots);

        var uniqueSlots = slots
            .GroupBy(slot => (slot.InstrumentId.Value.ToUpperInvariant(), slot.PositionIdx))
            .Select(group => group.First())
            .ToArray();
        if (uniqueSlots.Length == 0)
        {
            return [];
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT requested.instrument_id, requested.position_idx, latest.closed_at
            FROM unnest(@instrument_ids, @position_indices) AS requested(instrument_id, position_idx)
            JOIN LATERAL (
                SELECT p.closed_at
                FROM positions AS p
                INNER JOIN exchange_accounts AS account
                    ON account.exchange_account_id = p.exchange_account_id
                WHERE p.exchange_account_id = @exchange_account_id
                  AND account.user_id = @user_id
                  AND p.market_category = @market_category
                  AND p.tracking_state = 'Closed'
                  AND lower(p.instrument_id) = lower(requested.instrument_id)
                  AND p.position_idx = requested.position_idx
                ORDER BY p.closed_at DESC, p.position_id DESC
                LIMIT 1
            ) AS latest ON TRUE
            """;
        command.Parameters.Add(new NpgsqlParameter(
            "instrument_ids",
            uniqueSlots.Select(slot => slot.InstrumentId.Value).ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
        });
        command.Parameters.Add(new NpgsqlParameter(
            "position_indices",
            uniqueSlots.Select(slot => slot.PositionIdx).ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer,
        });
        command.Parameters.Add(new NpgsqlParameter("exchange_account_id", exchangeAccountId.Value));
        command.Parameters.Add(new NpgsqlParameter("user_id", userId.Value));
        command.Parameters.Add(new NpgsqlParameter("market_category", marketCategory.ToString()));

        var result = new List<LatestClosedPositionSlot>(uniqueSlots.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new LatestClosedPositionSlot(
                new PositionSlotKey(InstrumentId.From(reader.GetString(0)), reader.GetInt32(1)),
                PersistenceDateTime.ToUtc(reader.GetFieldValue<DateTimeOffset>(2))));
        }

        return result;
    }

    public async Task<Versioned<Position>?> GetByIdAsync(
        UserId userId,
        PositionId id,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var entity = await dbContext.Positions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                position =>
                    position.Id == id.Value &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value),
                cancellationToken);

        if (entity is null) return null;

        var changes = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change =>
                change.PositionId == id.Value &&
                dbContext.Positions.Any(position =>
                    position.Id == change.PositionId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value)))
            .OrderBy(change => change.Sequence)
            .ToArrayAsync(cancellationToken);

        return new Versioned<Position>(
            PositionMapper.ToDomain(entity, changes), new ConcurrencyVersion(entity.Version));
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        Position position,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        EnsureUserId(userId);

        var mapped = PositionMapper.ToEntity(position);
        var accountIsOwned = await dbContext.ExchangeAccounts.AnyAsync(
            account => account.Id == mapped.ExchangeAccountId && account.UserId == userId.Value,
            cancellationToken);
        if (!accountIsOwned) throw UnavailablePositionConflict(position.Id);

        if (expectedVersion is null)
        {
            var ownsTransaction = dbContext.Database.CurrentTransaction is null;
            var insertTransaction = ownsTransaction
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var insertVersion = ConcurrencyVersion.Initial;
            try
            {
                mapped.Version = insertVersion.Value;
                dbContext.Positions.Add(mapped);

                dbContext.PositionChanges.AddRange(
                    position.Changes.Select((change, index) => PositionChangeMapper.ToEntity(change, index + 1)));
                await dbContext.SaveChangesAsync(cancellationToken);
                if (insertTransaction is not null)
                {
                    await insertTransaction.CommitAsync(cancellationToken);
                }
            }
            catch (DbUpdateException exception)
                when (
                    PostgreSqlConcurrencyConflictDetector.IsDuplicatePrimaryKey(
                        exception,
                        "PK_positions"))
            {
                if (insertTransaction is not null)
                {
                    await insertTransaction.RollbackAsync(CancellationToken.None);
                }

                throw new ConcurrencyConflictException(
                    $"Position {position.Id} was inserted concurrently.",
                    exception);
            }
            catch
            {
                if (insertTransaction is not null)
                {
                    await insertTransaction.RollbackAsync(CancellationToken.None);
                }

                throw;
            }
            finally
            {
                if (insertTransaction is not null)
                {
                    await insertTransaction.DisposeAsync();
                }
            }

            return insertVersion;
        }

        var ownedPositionId = await dbContext.Positions
            .AsNoTracking()
            .Where(entity =>
                entity.Id == mapped.Id &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == entity.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .Select(entity => (Guid?)entity.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (ownedPositionId is null)
            throw UnavailablePositionConflict(position.Id);

        var ownsUpdateTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsUpdateTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var newVersion = expectedVersion.Value.Next();
        try
        {
            // Сначала получить ownership и version CAS, затем читать или подготавливать строки изменений.
            var affected = await dbContext.Positions
                .Where(entity =>
                    entity.Id == mapped.Id &&
                    entity.ExchangeAccountId == mapped.ExchangeAccountId &&
                    entity.Version == expectedVersion.Value.Value &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == entity.ExchangeAccountId &&
                        account.UserId == userId.Value))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(entity => entity.MarketCategory, mapped.MarketCategory)
                        .SetProperty(entity => entity.Size, mapped.Size)
                        .SetProperty(entity => entity.AverageEntryPrice, mapped.AverageEntryPrice)
                        .SetProperty(entity => entity.PositionValue, mapped.PositionValue)
                        .SetProperty(entity => entity.Leverage, mapped.Leverage)
                        .SetProperty(entity => entity.MarkPrice, mapped.MarkPrice)
                        .SetProperty(entity => entity.BreakEvenPrice, mapped.BreakEvenPrice)
                        .SetProperty(entity => entity.LiquidationPrice, mapped.LiquidationPrice)
                        .SetProperty(entity => entity.UnrealizedPnl, mapped.UnrealizedPnl)
                        .SetProperty(entity => entity.TakeProfit, mapped.TakeProfit)
                        .SetProperty(entity => entity.StopLoss, mapped.StopLoss)
                        .SetProperty(entity => entity.TrailingStop, mapped.TrailingStop)
                        .SetProperty(entity => entity.FirstDetectedAt, mapped.FirstDetectedAt)
                        .SetProperty(entity => entity.LastObservedAt, mapped.LastObservedAt)
                        .SetProperty(entity => entity.ClosedAt, mapped.ClosedAt)
                        .SetProperty(entity => entity.TrackingState, mapped.TrackingState)
                        .SetProperty(entity => entity.Version, newVersion.Value),
                    cancellationToken);

            if (affected != 1)
            {
                throw UnavailablePositionConflict(position.Id);
            }

            var persistedChanges = await dbContext.PositionChanges
                .AsNoTracking()
                .Where(change =>
                    change.PositionId == mapped.Id &&
                    dbContext.Positions.Any(entity =>
                        entity.Id == change.PositionId &&
                        entity.ExchangeAccountId == mapped.ExchangeAccountId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == entity.ExchangeAccountId &&
                            account.UserId == userId.Value)))
                .OrderBy(change => change.Sequence)
                .ToArrayAsync(cancellationToken);

            if (persistedChanges.Length > position.Changes.Count)
                throw new InvalidOperationException(
                    $"Position {position.Id} contains fewer history entries than the database.");

            for (var index = 0; index < persistedChanges.Length; index++)
            {
                if (!PositionChangeMapper.IsEquivalent(
                        persistedChanges[index], position.Changes[index], index + 1))
                    throw new InvalidOperationException(
                        $"Position {position.Id} history is not an append-only continuation.");
            }

            var newChanges = position.Changes
                .Skip(persistedChanges.Length)
                .Select((change, index) => PositionChangeMapper.ToEntity(
                    change, persistedChanges.Length + index + 1))
                .ToArray();

            if (newChanges.Length > 0)
            {
                dbContext.PositionChanges.AddRange(newChanges);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return newVersion;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static ConcurrencyConflictException UnavailablePositionConflict(
        PositionId id,
        Exception? innerException = null) =>
        innerException is null
            ? new($"Position {id} is unavailable in the requested user scope.")
            : new($"Position {id} is unavailable in the requested user scope.", innerException);

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
    }
}
