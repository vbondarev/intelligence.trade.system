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
                failure_code,
                version)
            SELECT account.exchange_account_id, 1, @status, NULL, NULL, NULL, TRUE, NULL, 1
            FROM exchange_accounts AS account
            WHERE account.exchange_account_id = @exchange_account_id
              AND account.user_id = @user_id
            ON CONFLICT (exchange_account_id) DO UPDATE
            SET generation = observation.generation + 1,
                transport_status = EXCLUDED.transport_status,
                last_heartbeat_at = NULL,
                recovery_required = TRUE,
                failure_code = NULL,
                version = observation.version + 1
            RETURNING
                observation.exchange_account_id,
                observation.generation,
                observation.transport_status,
                observation.last_connected_at,
                observation.last_heartbeat_at,
                observation.last_fully_reconciled_at,
                observation.recovery_required,
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

        return entity is null
            ? throw new ConcurrencyConflictException(
                $"Exchange account {exchangeAccountId} is unavailable in the requested user scope.")
            : ToVersioned(entity);
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
            FailureCode = await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetString(7),
            Version = reader.GetInt64(8),
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
