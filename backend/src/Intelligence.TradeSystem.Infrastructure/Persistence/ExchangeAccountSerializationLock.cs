using System.Data;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

internal static class ExchangeAccountSerializationLock
{
    public static async Task LockAsync(
        TradeSystemDbContext dbContext,
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken)
    {
        await EnsureTransactionAsync(dbContext, cancellationToken).ConfigureAwait(false);

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT exchange_account_id
            FROM exchange_accounts
            WHERE exchange_account_id = @exchange_account_id
              AND user_id = @user_id
            FOR UPDATE
            """;
        AddParameter(command, "exchange_account_id", exchangeAccountId.Value);
        AddParameter(command, "user_id", userId.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
            throw new ConcurrencyConflictException(
                $"Exchange account {exchangeAccountId} is unavailable in the requested user scope.");
    }

    public static async Task LockForPositionAsync(
        TradeSystemDbContext dbContext,
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken)
    {
        await EnsureTransactionAsync(dbContext, cancellationToken).ConfigureAwait(false);

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT a.exchange_account_id
            FROM positions AS p
            INNER JOIN exchange_accounts AS a
                ON a.exchange_account_id = p.exchange_account_id
            WHERE p.position_id = @position_id
              AND a.user_id = @user_id
            FOR UPDATE OF a
            """;
        AddParameter(command, "position_id", positionId.Value);
        AddParameter(command, "user_id", userId.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
            throw new ConcurrencyConflictException(
                $"Exchange account for position {positionId} is unavailable in the requested user scope.");
    }

    private static Task EnsureTransactionAsync(
        TradeSystemDbContext dbContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An exchange account serialization lock requires an active database transaction.");
        }

        return Task.CompletedTask;
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.Guid;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
