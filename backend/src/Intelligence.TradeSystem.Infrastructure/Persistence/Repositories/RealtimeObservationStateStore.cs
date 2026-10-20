using System.Data.Common;
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

internal sealed class RealtimeObservationStateStore(TradeSystemDbContext dbContext)
    : IRealtimeObservationStateStore
{
    public async Task<Versioned<RealtimeObservationState>?> GetAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);

        var entity = await dbContext.ExchangeRealtimeObservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                observation =>
                    observation.ExchangeAccountId == exchangeAccountId.Value &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == observation.ExchangeAccountId &&
                        account.UserId == userId.Value),
                cancellationToken);

        return entity is null ? null : ToVersioned(entity);
    }

    public async Task<Versioned<RealtimeObservationState>> AdvanceGenerationAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        RealtimeObservationTransportStatus status,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown transport status.");
        }

        // Одна команда атомарно создаёт первое поколение или увеличивает текущее; строка
        // предлагается только для аккаунта пользователя, поэтому чужой аккаунт не изменяется.
        // Строка аккаунта блокируется до вставки или изменения состояния, в порядке каскадного
        // удаления аккаунта: удалённый конкурентно аккаунт даёт пустую выборку, а не FK violation.
        // RecoveryRequestEpoch увеличивается вместе с поколенческим recovery-запросом и не
        // сбрасывается в ноль при переполнении long — такая запись отклоняется.
        var entity = await TradeSystemDbCommand.ExecuteAsync(
            dbContext,
            """
            INSERT INTO exchange_realtime_observations AS observation (
                exchange_account_id,
                generation,
                transport_status,
                last_connected_at,
                last_heartbeat_at,
                last_fully_reconciled_at,
                recovery_required,
                recovery_request_epoch,
                failure_code,
                version)
            SELECT account.exchange_account_id, 1, @status, NULL, NULL, NULL, TRUE, 1, NULL, 1
            FROM exchange_accounts AS account
            WHERE account.exchange_account_id = @exchange_account_id
              AND account.user_id = @user_id
            FOR KEY SHARE OF account
            ON CONFLICT (exchange_account_id) DO UPDATE
            SET generation = observation.generation + 1,
                transport_status = EXCLUDED.transport_status,
                last_heartbeat_at = NULL,
                recovery_required = TRUE,
                recovery_request_epoch = observation.recovery_request_epoch + 1,
                failure_code = NULL,
                version = observation.version + 1
            WHERE observation.recovery_request_epoch < 9223372036854775807
            RETURNING
                observation.exchange_account_id,
                observation.generation,
                observation.transport_status,
                observation.last_connected_at,
                observation.last_heartbeat_at,
                observation.last_fully_reconciled_at,
                observation.recovery_required,
                observation.recovery_request_epoch,
                observation.failure_code,
                observation.version
            """,
            [
                Parameter("exchange_account_id", NpgsqlDbType.Uuid, exchangeAccountId.Value),
                Parameter("user_id", NpgsqlDbType.Uuid, userId.Value),
                Parameter("status", NpgsqlDbType.Varchar, status.ToString()),
            ],
            ReadSingleOrDefaultAsync,
            cancellationToken);

        if (entity is null)
        {
            var existing = await GetAsync(userId, exchangeAccountId, cancellationToken);
            if (existing is { Value.RecoveryRequestEpoch: long.MaxValue })
            {
                throw new InvalidOperationException(
                    $"RecoveryRequestEpoch of exchange account {exchangeAccountId} would overflow.");
            }

            throw new ConcurrencyConflictException(
                $"Exchange account {exchangeAccountId} is unavailable in the requested user scope.");
        }

        return ToVersioned(entity);
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        RealtimeObservationState state,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureIdentity(userId, state.ExchangeAccountId);

        var nextVersion = expectedVersion.Next();
        var affected = await TradeSystemDbCommand.ExecuteNonQueryAsync(
            dbContext,
            """
            UPDATE exchange_realtime_observations AS observation
            SET transport_status = @status,
                last_connected_at = @last_connected_at,
                last_heartbeat_at = @last_heartbeat_at,
                last_fully_reconciled_at = @last_fully_reconciled_at,
                recovery_required = @recovery_required,
                recovery_request_epoch = @recovery_request_epoch,
                failure_code = @failure_code,
                version = @next_version
            FROM exchange_accounts AS account
            WHERE observation.exchange_account_id = @exchange_account_id
              AND account.exchange_account_id = observation.exchange_account_id
              AND account.user_id = @user_id
              AND observation.generation = @generation
              AND observation.version = @expected_version
            """,
            [
                Parameter("exchange_account_id", NpgsqlDbType.Uuid, state.ExchangeAccountId.Value),
                Parameter("user_id", NpgsqlDbType.Uuid, userId.Value),
                Parameter("generation", NpgsqlDbType.Bigint, state.Generation),
                Parameter("expected_version", NpgsqlDbType.Bigint, expectedVersion.Value),
                Parameter("next_version", NpgsqlDbType.Bigint, nextVersion.Value),
                Parameter("status", NpgsqlDbType.Varchar, state.TransportStatus.ToString()),
                Parameter("last_connected_at", NpgsqlDbType.TimestampTz, PersistenceDateTime.ToUtc(state.LastConnectedAt)),
                Parameter("last_heartbeat_at", NpgsqlDbType.TimestampTz, PersistenceDateTime.ToUtc(state.LastHeartbeatAt)),
                Parameter(
                    "last_fully_reconciled_at",
                    NpgsqlDbType.TimestampTz,
                    PersistenceDateTime.ToUtc(state.LastFullyReconciledAt)),
                Parameter("recovery_required", NpgsqlDbType.Boolean, state.RecoveryRequired),
                Parameter("recovery_request_epoch", NpgsqlDbType.Bigint, state.RecoveryRequestEpoch),
                Parameter("failure_code", NpgsqlDbType.Varchar, state.FailureCode),
            ],
            cancellationToken);

        if (affected != 1)
        {
            throw new ConcurrencyConflictException(
                $"Realtime observation state of exchange account {state.ExchangeAccountId} was modified, " +
                "superseded by a newer generation or is unavailable in the requested user scope.");
        }

        return nextVersion;
    }

    public async Task<RealtimeObservationStateLockResult> TryLockGenerationAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Realtime observation generation lock requires an active database transaction.");
        }

        var locked = await TradeSystemDbCommand.ExecuteAsync(
            dbContext,
            """
            WITH account_lock AS (
                SELECT account.exchange_account_id
                FROM exchange_accounts AS account
                WHERE account.exchange_account_id = @exchange_account_id
                  AND account.user_id = @user_id
                FOR KEY SHARE OF account
            )
            SELECT observation.recovery_required
            FROM exchange_realtime_observations AS observation
            INNER JOIN account_lock ON account_lock.exchange_account_id = observation.exchange_account_id
            WHERE observation.generation = @generation
            FOR SHARE OF observation
            """,
            [
                Parameter("exchange_account_id", NpgsqlDbType.Uuid, exchangeAccountId.Value),
                Parameter("user_id", NpgsqlDbType.Uuid, userId.Value),
                Parameter("generation", NpgsqlDbType.Bigint, generation),
            ],
            static async (command, token) =>
            {
                await using var reader = await command.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token))
                {
                    return RealtimeObservationStateLockResult.GenerationSuperseded;
                }

                return reader.GetBoolean(0)
                    ? RealtimeObservationStateLockResult.RecoveryRequired
                    : RealtimeObservationStateLockResult.Locked;
            },
            cancellationToken);

        return locked;
    }

    private static async Task<ExchangeRealtimeObservationEntity?> ReadSingleOrDefaultAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExchangeRealtimeObservationEntity
        {
            ExchangeAccountId = reader.GetGuid(0),
            Generation = reader.GetInt64(1),
            TransportStatus = Enum.Parse<RealtimeObservationTransportStatus>(reader.GetString(2)),
            LastConnectedAt = await ReadNullableTimestampAsync(reader, 3, cancellationToken),
            LastHeartbeatAt = await ReadNullableTimestampAsync(reader, 4, cancellationToken),
            LastFullyReconciledAt = await ReadNullableTimestampAsync(reader, 5, cancellationToken),
            RecoveryRequired = reader.GetBoolean(6),
            RecoveryRequestEpoch = reader.GetInt64(7),
            FailureCode = await reader.IsDBNullAsync(8, cancellationToken) ? null : reader.GetString(8),
            Version = reader.GetInt64(9),
        };
    }

    private static async Task<DateTimeOffset?> ReadNullableTimestampAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static Versioned<RealtimeObservationState> ToVersioned(ExchangeRealtimeObservationEntity entity) =>
        new(
            new RealtimeObservationState
            {
                ExchangeAccountId = ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
                Generation = entity.Generation,
                TransportStatus = entity.TransportStatus,
                LastConnectedAt = entity.LastConnectedAt,
                LastHeartbeatAt = entity.LastHeartbeatAt,
                LastFullyReconciledAt = entity.LastFullyReconciledAt,
                RecoveryRequired = entity.RecoveryRequired,
                RecoveryRequestEpoch = entity.RecoveryRequestEpoch,
                FailureCode = entity.FailureCode,
            },
            new ConcurrencyVersion(entity.Version));

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static void EnsureIdentity(UserId userId, ExchangeAccountId exchangeAccountId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }
    }
}
