using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

/// <summary>
/// Выполняет SQL-команду с явно типизированными параметрами в соединении и текущей транзакции
/// <see cref="TradeSystemDbContext"/>.
/// </summary>
/// <remarks>
/// Явные типы параметров нужны для <see langword="null"/>-значений и массивов: PostgreSQL не
/// выводит их тип из контекста условий сравнения и <c>unnest</c>.
/// </remarks>
internal static class TradeSystemDbCommand
{
    public static async Task<T> ExecuteAsync<T>(
        TradeSystemDbContext dbContext,
        string commandText,
        IEnumerable<NpgsqlParameter> parameters,
        Func<DbCommand, CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            return await execute(command, cancellationToken);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public static Task<int> ExecuteNonQueryAsync(
        TradeSystemDbContext dbContext,
        string commandText,
        IEnumerable<NpgsqlParameter> parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            dbContext,
            commandText,
            parameters,
            static (command, token) => command.ExecuteNonQueryAsync(token),
            cancellationToken);
}
