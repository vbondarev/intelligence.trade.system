using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

internal sealed class PositionObservationCursorStore(TradeSystemDbContext dbContext)
    : IPositionObservationCursorStore
{
    // Строка аккаунта блокируется отдельной командой раньше строки состояния: так порядок
    // блокировок account → состояние наблюдения совпадает с порядком каскадного удаления аккаунта,
    // а FK-проверка вставки курсора не ждёт строку аккаунта, удерживая строку состояния.
    private const string AccountLock =
        """
        SELECT 1
        FROM exchange_accounts
        WHERE exchange_account_id = @exchange_account_id
          AND user_id = @user_id
        FOR KEY SHARE
        """;

    // Разделяемая блокировка строки состояния удерживается до конца транзакции: смена поколения
    // (UPDATE этой строки) ждёт фиксации записи курсора, а уже зафиксированная смена поколения
    // делает fence пустым при повторной проверке условия в READ COMMITTED.
    private const string GenerationFence =
        """
        WITH fence AS (
            SELECT observation.exchange_account_id
            FROM exchange_realtime_observations AS observation
            INNER JOIN exchange_accounts AS account
                ON account.exchange_account_id = observation.exchange_account_id
            WHERE observation.exchange_account_id = @exchange_account_id
              AND account.user_id = @user_id
              AND observation.generation = @generation
            FOR SHARE OF observation
        )
        """;

    public async Task<Versioned<PositionObservationCursor>?> GetAsync(
        UserId userId,
        PositionObservationCursorKey key,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureKey(key);

        var category = key.Category;
        var symbol = key.Symbol.Value;
        var entity = await OwnedCursors(userId, key.ExchangeAccountId)
            .SingleOrDefaultAsync(
                cursor =>
                    cursor.MarketCategory == category &&
                    cursor.InstrumentId == symbol &&
                    cursor.PositionIdx == key.PositionIdx,
                cancellationToken);

        return entity is null ? null : ToVersioned(entity);
    }

    public async Task<IReadOnlyCollection<Versioned<PositionObservationCursor>>> GetByExchangeAccountAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }

        var entities = await OwnedCursors(userId, exchangeAccountId)
            .OrderBy(cursor => cursor.MarketCategory)
            .ThenBy(cursor => cursor.InstrumentId)
            .ThenBy(cursor => cursor.PositionIdx)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToVersioned).ToArray();
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        PositionObservationCursor cursor,
        ConcurrencyVersion? expectedVersion,
        long expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        EnsureUserId(userId);
        EnsureKey(cursor.Key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration);
        if (!cursor.HasRevision)
        {
            throw new ArgumentException(
                "A cursor requires a provider sequence or a provider update time.",
                nameof(cursor));
        }

        if (!cursor.HasValidDirectAppliedPositionStamp)
        {
            throw new ArgumentException(
                "A direct-applied position stamp requires a position ID and positive version.",
                nameof(cursor));
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await LockAccountAndWriteAsync(
                userId,
                cursor,
                expectedVersion,
                expectedGeneration,
                cancellationToken);
        }

        // Блокировка аккаунта отдельной командой действует только внутри транзакции.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var version = await LockAccountAndWriteAsync(
            userId,
            cursor,
            expectedVersion,
            expectedGeneration,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return version;
    }

    private async Task<ConcurrencyVersion> LockAccountAndWriteAsync(
        UserId userId,
        PositionObservationCursor cursor,
        ConcurrencyVersion? expectedVersion,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        var lockedAccount = await TradeSystemDbCommand.ExecuteAsync(
            dbContext,
            AccountLock,
            [
                Parameter("exchange_account_id", NpgsqlDbType.Uuid, cursor.Key.ExchangeAccountId.Value),
                Parameter("user_id", NpgsqlDbType.Uuid, userId.Value),
            ],
            static (command, token) => command.ExecuteScalarAsync(token),
            cancellationToken);
        if (lockedAccount is null)
        {
            throw new ConcurrencyConflictException(
                $"Exchange account {cursor.Key.ExchangeAccountId} is unavailable in the requested user scope.");
        }

        var nextVersion = expectedVersion is { } current ? current.Next() : ConcurrencyVersion.Initial;
        var parameters = new List<NpgsqlParameter>
        {
            Parameter("exchange_account_id", NpgsqlDbType.Uuid, cursor.Key.ExchangeAccountId.Value),
            Parameter("user_id", NpgsqlDbType.Uuid, userId.Value),
            Parameter("generation", NpgsqlDbType.Bigint, expectedGeneration),
            Parameter("market_category", NpgsqlDbType.Varchar, cursor.Key.Category.ToString()),
            Parameter("instrument_id", NpgsqlDbType.Varchar, cursor.Key.Symbol.Value),
            Parameter("position_idx", NpgsqlDbType.Integer, cursor.Key.PositionIdx),
            Parameter("last_provider_sequence", NpgsqlDbType.Bigint, cursor.LastProviderSequence),
            Parameter(
                "last_provider_updated_at",
                NpgsqlDbType.TimestampTz,
                PersistenceDateTime.ToUtc(cursor.LastProviderUpdatedAt)),
            Parameter(
                "last_message_created_at",
                NpgsqlDbType.TimestampTz,
                PersistenceDateTime.ToUtc(cursor.LastMessageCreatedAt)),
            Parameter("last_accepted_fingerprint", NpgsqlDbType.Varchar, cursor.LastAcceptedFingerprint),
            Parameter(
                "last_direct_applied_position_id",
                NpgsqlDbType.Uuid,
                cursor.LastDirectAppliedPositionId?.Value),
            Parameter(
                "last_direct_applied_position_version",
                NpgsqlDbType.Bigint,
                cursor.LastDirectAppliedPositionVersion?.Value),
            Parameter("next_version", NpgsqlDbType.Bigint, nextVersion.Value),
        };

        int affected;
        if (expectedVersion is null)
        {
            affected = await TradeSystemDbCommand.ExecuteNonQueryAsync(
                dbContext,
                GenerationFence +
                """
                INSERT INTO position_observation_cursors (
                    exchange_account_id,
                    market_category,
                    instrument_id,
                    position_idx,
                    last_provider_sequence,
                    last_provider_updated_at,
                    last_message_created_at,
                    last_accepted_fingerprint,
                    last_direct_applied_position_id,
                    last_direct_applied_position_version,
                    version)
                SELECT
                    fence.exchange_account_id,
                    @market_category,
                    @instrument_id,
                    @position_idx,
                    @last_provider_sequence,
                    @last_provider_updated_at,
                    @last_message_created_at,
                    @last_accepted_fingerprint,
                    @last_direct_applied_position_id,
                    @last_direct_applied_position_version,
                    @next_version
                FROM fence
                ON CONFLICT (exchange_account_id, market_category, instrument_id, position_idx) DO NOTHING
                """,
                parameters,
                cancellationToken);
        }
        else
        {
            parameters.Add(Parameter("expected_version", NpgsqlDbType.Bigint, expectedVersion.Value.Value));
            // Сравнение с NULL-параметром даёт NULL, поэтому известный компонент ревизии нельзя
            // ни уменьшить, ни сбросить.
            affected = await TradeSystemDbCommand.ExecuteNonQueryAsync(
                dbContext,
                GenerationFence +
                """
                UPDATE position_observation_cursors AS stored
                SET last_provider_sequence = @last_provider_sequence,
                    last_provider_updated_at = @last_provider_updated_at,
                    last_message_created_at = @last_message_created_at,
                    last_accepted_fingerprint = @last_accepted_fingerprint,
                    last_direct_applied_position_id = @last_direct_applied_position_id,
                    last_direct_applied_position_version = @last_direct_applied_position_version,
                    version = @next_version
                FROM fence
                WHERE stored.exchange_account_id = fence.exchange_account_id
                  AND stored.market_category = @market_category
                  AND stored.instrument_id = @instrument_id
                  AND stored.position_idx = @position_idx
                  AND stored.version = @expected_version
                  AND (stored.last_provider_sequence IS NULL
                       OR stored.last_provider_sequence <= @last_provider_sequence)
                  AND (stored.last_provider_updated_at IS NULL
                       OR stored.last_provider_updated_at <= @last_provider_updated_at)
                """,
                parameters,
                cancellationToken);
        }

        if (affected != 1)
        {
            throw new ConcurrencyConflictException(
                $"Position observation cursor {cursor.Key} already exists, was modified, would regress, " +
                "belongs to a superseded observation generation or is unavailable in the requested user scope.");
        }

        return nextVersion;
    }

    private IQueryable<PositionObservationCursorEntity> OwnedCursors(
        UserId userId,
        ExchangeAccountId exchangeAccountId) =>
        dbContext.PositionObservationCursors
            .AsNoTracking()
            .Where(cursor =>
                cursor.ExchangeAccountId == exchangeAccountId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == cursor.ExchangeAccountId &&
                    account.UserId == userId.Value));

    private static Versioned<PositionObservationCursor> ToVersioned(PositionObservationCursorEntity entity) =>
        new(
            new PositionObservationCursor
            {
                Key = PositionObservationCursorKey.Create(
                    ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
                    entity.MarketCategory,
                    InstrumentId.From(entity.InstrumentId),
                    entity.PositionIdx),
                LastProviderSequence = entity.LastProviderSequence,
                LastProviderUpdatedAt = entity.LastProviderUpdatedAt,
                LastMessageCreatedAt = entity.LastMessageCreatedAt,
                LastAcceptedFingerprint = entity.LastAcceptedFingerprint,
                LastDirectAppliedPositionId = entity.LastDirectAppliedPositionId is { } positionId
                    ? PositionId.FromGuid(positionId)
                    : null,
                LastDirectAppliedPositionVersion = entity.LastDirectAppliedPositionVersion is { } positionVersion
                    ? new ConcurrencyVersion(positionVersion)
                    : null,
            },
            new ConcurrencyVersion(entity.Version));

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }

    private static void EnsureKey(PositionObservationCursorKey key)
    {
        if (key.ExchangeAccountId == default)
        {
            throw new ArgumentException("Cursor key must be initialized.", nameof(key));
        }
    }
}
