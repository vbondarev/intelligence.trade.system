using System.Data.Common;
using System.Diagnostics;
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
    private static readonly AsyncLocal<Action<DbCommand, TimeSpan>?> ExecutionObserver = new();

    internal static IDisposable CaptureExecution(Action<DbCommand, TimeSpan> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var previous = ExecutionObserver.Value;
        ExecutionObserver.Value = observer;
        return new ExecutionObserverScope(previous);
    }

    public static async Task<T> ExecuteAsync<T>(
        TradeSystemDbContext dbContext,
        string commandText,
        IEnumerable<NpgsqlParameter> parameters,
        Func<DbCommand, CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            var observer = ExecutionObserver.Value;
            if (observer is null)
            {
                return await execute(command, cancellationToken).ConfigureAwait(false);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await execute(command, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                stopwatch.Stop();
                observer(command, stopwatch.Elapsed);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
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

    private sealed class ExecutionObserverScope(
        Action<DbCommand, TimeSpan>? previous) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                ExecutionObserver.Value = previous;
            }
        }
    }
}
