using System.Data;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

internal static class RecommendationRecommendationLock
{
    public static async Task LockAsync(
        TradeSystemDbContext dbContext,
        UserId userId,
        PositionId positionId,
        RecommendationId recommendationId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT r.recommendation_id
            FROM recommendations AS r
            INNER JOIN positions AS p
                ON p.position_id = r.position_id
            INNER JOIN exchange_accounts AS a
                ON a.exchange_account_id = p.exchange_account_id
            WHERE r.recommendation_id = @recommendation_id
              AND r.position_id = @position_id
              AND a.user_id = @user_id
            FOR UPDATE OF r
            """;

        AddParameter(command, "recommendation_id", recommendationId.Value);
        AddParameter(command, "position_id", positionId.Value);
        AddParameter(command, "user_id", userId.Value);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
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
