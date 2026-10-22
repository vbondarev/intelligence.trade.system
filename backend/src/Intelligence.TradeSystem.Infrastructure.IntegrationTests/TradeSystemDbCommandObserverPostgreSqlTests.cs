using System.Data;
using System.Data.Common;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class TradeSystemDbCommandObserverPostgreSqlTests
{
    [Fact]
    public async Task Execution_observer_is_scoped_and_only_measures_observed_commands()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tradesystem")
            .WithUsername("tradesystem")
            .WithPassword("tradesystem")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var context = new TradeSystemDbContext(options);
        var connection = context.Database.GetDbConnection();
        var outerDurations = new List<TimeSpan>();
        var innerDurations = new List<TimeSpan>();
        var allObserverCalls = 0;

        Assert.Equal(1, await ExecuteScalarAsync(context, "SELECT 1"));
        Assert.Equal(ConnectionState.Closed, connection.State);

        using (TradeSystemDbCommand.CaptureExecution((_, elapsed) =>
               {
                   outerDurations.Add(elapsed);
                   allObserverCalls++;
               }))
        {
            Assert.Equal(1, await ExecuteScalarAsync(context, "SELECT 1"));
            using (TradeSystemDbCommand.CaptureExecution((_, elapsed) =>
                   {
                       innerDurations.Add(elapsed);
                       allObserverCalls++;
                   }))
            {
                Assert.Equal(1, await ExecuteScalarAsync(context, "SELECT 1"));
            }

            Assert.Equal(1, await ExecuteScalarAsync(context, "SELECT 1"));
        }

        Assert.Equal(2, outerDurations.Count);
        Assert.Single(innerDurations);
        Assert.All(outerDurations.Concat(innerDurations), elapsed => Assert.True(elapsed > TimeSpan.Zero));
        Assert.Equal(ConnectionState.Closed, connection.State);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var expectedTransaction = transaction.GetDbTransaction();
            using var transactionObserver = TradeSystemDbCommand.CaptureExecution(
                (command, _) => Assert.Same(expectedTransaction, command.Transaction));
            Assert.Equal(
                1,
                await ExecuteAsync(
                    context,
                    "SELECT 1",
                    (command, token) => command.ExecuteScalarAsync(token)));
            await transaction.RollbackAsync();
        }

        var exceptionDurations = new List<TimeSpan>();
        var sqlException = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            using var exceptionObserver = TradeSystemDbCommand.CaptureExecution(
                (_, elapsed) => exceptionDurations.Add(elapsed));
            await ExecuteScalarAsync(context, "SELECT 1 / 0");
        });
        Assert.Equal("22012", sqlException.SqlState);
        Assert.Single(exceptionDurations);
        Assert.True(exceptionDurations[0] > TimeSpan.Zero);
        Assert.Equal(ConnectionState.Closed, connection.State);

        using var cancellation = new CancellationTokenSource();
        var cancellationObserverCalls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var cancellationObserver = TradeSystemDbCommand.CaptureExecution(
                (_, _) => cancellationObserverCalls++);
            await TradeSystemDbCommand.ExecuteAsync(
                context,
                "SELECT 1",
                Array.Empty<NpgsqlParameter>(),
                (command, token) =>
                {
                    cancellation.Cancel();
                    return command.ExecuteScalarAsync(token);
                },
                cancellation.Token);
        });
        Assert.Equal(1, cancellationObserverCalls);
        Assert.Equal(ConnectionState.Closed, connection.State);

        await ExecuteScalarAsync(context, "SELECT 1");
        Assert.Equal(3, allObserverCalls);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private static Task<object?> ExecuteScalarAsync(
        TradeSystemDbContext context,
        string commandText) =>
        ExecuteAsync(
            context,
            commandText,
            static (command, cancellationToken) => command.ExecuteScalarAsync(cancellationToken));

    private static Task<T> ExecuteAsync<T>(
        TradeSystemDbContext context,
        string commandText,
        Func<DbCommand, CancellationToken, Task<T>> execute) =>
        TradeSystemDbCommand.ExecuteAsync(
            context,
            commandText,
            Array.Empty<NpgsqlParameter>(),
            execute,
            CancellationToken.None);
}
