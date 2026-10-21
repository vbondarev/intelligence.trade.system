using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using Xunit.Abstractions;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class PositionHistoryReadPerformancePostgreSqlTests
{
    private static readonly string[] LookupSymbols = ["BTCUSDT"];
    private static readonly int[] LookupPositionIndices = [0];
    private readonly ITestOutputHelper output;

    public PositionHistoryReadPerformancePostgreSqlTests(ITestOutputHelper output) =>
        this.output = output;

    [PositionHistoryReadPerformanceFact]
    public async Task Current_position_reads_avoid_materializing_closed_lifecycle_history()
    {
        await using var fixture = new PositionHistoryReadPerformanceFixture();
        await fixture.StartAsync();

        foreach (var scenario in fixture.Scenarios)
        {
            var userId = UserId.FromGuid(scenario.UserId);
            var accountId = ExchangeAccountId.FromGuid(scenario.AccountId);
            var baselineCapture = new QueryCapture();
            IReadOnlyCollection<Versioned<Position>> baselinePositions;
            var baselineClock = Stopwatch.StartNew();
            await using (var context = fixture.CreateContext(baselineCapture))
            {
                baselinePositions = await new PositionRepository(context)
                    .GetByExchangeAccountAsync(userId, accountId);
            }

            baselineClock.Stop();
            var baselineRows = baselinePositions.Count +
                baselinePositions.Sum(position => position.Value.Changes.Count);
            var baselineQueryMs = baselineCapture.QueryDurations.Sum(duration => duration.TotalMilliseconds);
            var baselineLocks = await MeasureLegacyLocksAsync(fixture, userId, accountId);

            var optimizedCapture = new QueryCapture();
            IReadOnlyCollection<PositionVersionWatermark> watermarks;
            IReadOnlyCollection<PositionPortfolioProjection> projections;
            IReadOnlyCollection<Versioned<Position>> currentPositions;
            IReadOnlyCollection<LatestClosedPositionSlot> latestClosed;
            var latestClosedClock = new Stopwatch();
            var optimizedClock = Stopwatch.StartNew();
            await using (var context = fixture.CreateContext(optimizedCapture))
            {
                var repository = new PositionRepository(context);
                watermarks = await repository.GetCurrentVersionWatermarksAsync(userId, accountId);
                projections = await repository.GetCurrentPortfolioPositionsAsync(userId, accountId);
                currentPositions = await repository.GetCurrentWithHistoryAsync(userId, accountId);
                latestClosedClock.Start();
                latestClosed = await repository.GetLatestClosedSlotsAsync(
                    userId,
                    accountId,
                    MarketCategory.Linear,
                    [new PositionSlotKey(InstrumentId.From("BTCUSDT"), 0)]);
            }

            latestClosedClock.Stop();
            optimizedClock.Stop();
            var optimizedRows = watermarks.Count +
                projections.Count +
                currentPositions.Count +
                currentPositions.Sum(position => position.Value.Changes.Count) +
                latestClosed.Count;
            var optimizedQueryMs = optimizedCapture.QueryDurations.Sum(duration => duration.TotalMilliseconds);
            var optimizedLocks = await MeasureCurrentLocksAsync(fixture, userId, accountId);

            Assert.Equal(scenario.ClosedLifecycleCount + 5, baselinePositions.Count);
            Assert.Equal(5, watermarks.Count);
            Assert.Equal(5, projections.Count);
            Assert.Equal(5, currentPositions.Count);
            Assert.Single(latestClosed);
            Assert.Equal(2, baselineCapture.Commands.Count);
            Assert.Equal(4, optimizedCapture.Commands.Count);
            Assert.Equal(scenario.ClosedLifecycleCount + 5, baselineLocks.RowsLocked);
            Assert.Equal(5, optimizedLocks.RowsLocked);

            output.WriteLine(
                $"POSITION_HISTORY_DATASET closed={scenario.ClosedLifecycleCount} " +
                $"changes={scenario.ClosedLifecycleCount * 10} active=5");
            output.WriteLine(
                $"POSITION_HISTORY_METRIC dataset={scenario.Name} path=baseline " +
                $"round_trips={baselineCapture.Commands.Count} materialized_rows={baselineRows} " +
                $"command_ms={baselineQueryMs:F3} elapsed_ms={baselineClock.Elapsed.TotalMilliseconds:F3} " +
                $"lock_rows={baselineLocks.RowsLocked} lock_ms={baselineLocks.Duration.TotalMilliseconds:F3}");
            output.WriteLine(
                $"POSITION_HISTORY_METRIC dataset={scenario.Name} path=current " +
                $"round_trips={optimizedCapture.Commands.Count + 1} materialized_rows={optimizedRows} " +
                $"command_ms={optimizedQueryMs + latestClosedClock.Elapsed.TotalMilliseconds:F3} " +
                $"elapsed_ms={optimizedClock.Elapsed.TotalMilliseconds:F3} " +
                $"lock_rows={optimizedLocks.RowsLocked} lock_ms={optimizedLocks.Duration.TotalMilliseconds:F3}");

            await WritePlansAsync(
                fixture.ConnectionString,
                output,
                scenario.Name,
                "baseline",
                baselineCapture.Commands);
            await WritePlansAsync(
                fixture.ConnectionString,
                output,
                scenario.Name,
                "current",
                optimizedCapture.Commands);
            var latestClosedPlan = await ExplainLatestClosedLookupAsync(
                fixture.ConnectionString,
                userId.Value,
                accountId.Value);
            if (scenario.ClosedLifecycleCount == 10_000)
            {
                Assert.Contains("ix_positions_latest_closed_slot", GetIndexNames(latestClosedPlan));
            }

            output.WriteLine(FormatPlan(
                scenario.Name,
                "current",
                "latest-closed",
                latestClosedPlan));
        }
    }

    private static async Task<LockMeasurement> MeasureLegacyLocksAsync(
        PositionHistoryReadPerformanceFixture fixture,
        UserId userId,
        ExchangeAccountId accountId)
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var clock = Stopwatch.StartNew();
        var rowsLocked = 0;
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                SELECT p.position_id
                FROM positions AS p
                INNER JOIN exchange_accounts AS a ON a.exchange_account_id = p.exchange_account_id
                WHERE p.exchange_account_id = @exchange_account_id
                  AND a.user_id = @user_id
                ORDER BY p.position_id
                FOR UPDATE OF p
                """;
            command.Parameters.Add(new NpgsqlParameter("exchange_account_id", accountId.Value));
            command.Parameters.Add(new NpgsqlParameter("user_id", userId.Value));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rowsLocked++;
            }
        }

        var transactionLock = new ExchangeAccountSyncTransaction(context);
        await transactionLock.LockAccountAsync(userId, accountId);
        clock.Stop();
        await transaction.CommitAsync();
        return new LockMeasurement(rowsLocked, clock.Elapsed);
    }

    private static async Task<LockMeasurement> MeasureCurrentLocksAsync(
        PositionHistoryReadPerformanceFixture fixture,
        UserId userId,
        ExchangeAccountId accountId)
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var clock = Stopwatch.StartNew();
        var transactionLock = new ExchangeAccountSyncTransaction(context);
        await transactionLock.LockPositionsAsync(userId, accountId);
        await transactionLock.LockAccountAsync(userId, accountId);
        clock.Stop();
        await transaction.CommitAsync();
        return new LockMeasurement(5, clock.Elapsed);
    }

    private static async Task WritePlansAsync(
        string connectionString,
        ITestOutputHelper output,
        string dataset,
        string path,
        IReadOnlyCollection<CapturedCommand> commands)
    {
        var index = 0;
        foreach (var captured in commands)
        {
            var plan = await ExplainAsync(connectionString, captured);
            output.WriteLine(FormatPlan(dataset, path, $"query-{++index}", plan));
        }
    }

    private static async Task<string> ExplainLatestClosedLookupAsync(
        string connectionString,
        Guid userId,
        Guid accountId)
    {
        var query = new CapturedCommand(
            """
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
            """,
            [
                new NpgsqlParameter("instrument_ids", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = LookupSymbols,
                },
                new NpgsqlParameter("position_indices", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                {
                    Value = LookupPositionIndices,
                },
                new NpgsqlParameter("exchange_account_id", accountId),
                new NpgsqlParameter("user_id", userId),
                new NpgsqlParameter("market_category", "Linear"),
            ]);
        return await ExplainAsync(connectionString, query);
    }

    private static async Task<string> ExplainAsync(
        string connectionString,
        CapturedCommand captured)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + captured.CommandText;
        foreach (var parameter in captured.Parameters)
        {
            command.Parameters.Add(parameter.Clone());
        }

        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PostgreSQL returned an empty EXPLAIN result."));
    }

    private static string FormatPlan(
        string dataset,
        string path,
        string queryName,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        var plan = document.RootElement[0].GetProperty("Plan");
        var executionMs = document.RootElement[0].GetProperty("Execution Time").GetDouble();
        var rows = plan.GetProperty("Actual Rows").GetDouble();
        var hits = plan.TryGetProperty("Shared Hit Blocks", out var hitValue) ? hitValue.GetInt64() : 0;
        var reads = plan.TryGetProperty("Shared Read Blocks", out var readValue) ? readValue.GetInt64() : 0;
        var indexes = string.Join(',', GetIndexNames(json));
        return $"POSITION_HISTORY_EXPLAIN dataset={dataset} path={path} query={queryName} " +
            $"node={plan.GetProperty("Node Type").GetString()} rows={rows:F0} " +
            $"execution_ms={executionMs:F3} shared_hit_blocks={hits} shared_read_blocks={reads} " +
            $"indexes=[{indexes}]";
    }

    private static HashSet<string> GetIndexNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Visit(document.RootElement[0].GetProperty("Plan"));
        return names;

        void Visit(JsonElement node)
        {
            if (node.TryGetProperty("Index Name", out var indexName))
            {
                names.Add(indexName.GetString()!);
            }

            if (node.TryGetProperty("Plans", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    Visit(child);
                }
            }
        }
    }

    private sealed class QueryCapture : DbCommandInterceptor
    {
        private readonly Dictionary<Guid, Stopwatch> clocks = [];

        public List<CapturedCommand> Commands { get; } = [];
        public List<TimeSpan> QueryDurations { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(CapturedCommand.Create(command));
            clocks[eventData.CommandId] = Stopwatch.StartNew();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (clocks.Remove(eventData.CommandId, out var clock))
            {
                clock.Stop();
                QueryDurations.Add(clock.Elapsed);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed record CapturedCommand(
        string CommandText,
        IReadOnlyCollection<NpgsqlParameter> Parameters)
    {
        public static CapturedCommand Create(DbCommand command) =>
            new(
                command.CommandText,
                command.Parameters.Cast<NpgsqlParameter>()
                    .Select(parameter => (NpgsqlParameter)parameter.Clone())
                    .ToArray());
    }

    private sealed record LockMeasurement(
        int RowsLocked,
        TimeSpan Duration);
}

internal sealed class PositionHistoryReadPerformanceFactAttribute : FactAttribute
{
    public PositionHistoryReadPerformanceFactAttribute()
    {
        DisplayName = "Position history read performance benchmark";
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ITS_RUN_POSITION_HISTORY_PERFORMANCE"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set ITS_RUN_POSITION_HISTORY_PERFORMANCE=1 to run the PostgreSQL history benchmark.";
        }
    }
}
