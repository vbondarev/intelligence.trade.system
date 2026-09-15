using System.Data;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

internal static class RecommendationPositionLock
{
    public static async Task LockAsync(
        TradeSystemDbContext dbContext,
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT p.position_id
            FROM positions AS p
            INNER JOIN exchange_accounts AS a
                ON a.exchange_account_id = p.exchange_account_id
            WHERE p.position_id = @position_id
              AND a.user_id = @user_id
            FOR UPDATE OF p
            """;
        var positionParameter = command.CreateParameter();
        positionParameter.ParameterName = "position_id";
        positionParameter.DbType = DbType.Guid;
        positionParameter.Value = positionId.Value;
        command.Parameters.Add(positionParameter);
        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "user_id";
        userParameter.DbType = DbType.Guid;
        userParameter.Value = userId.Value;
        command.Parameters.Add(userParameter);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
            throw new ConcurrencyConflictException(
                $"Position {positionId} is unavailable in the requested user scope.");
    }
}
