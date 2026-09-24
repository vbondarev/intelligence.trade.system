using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class PositionListPerformancePostgreSqlTests
{
    private readonly ITestOutputHelper output;

    public PositionListPerformancePostgreSqlTests(ITestOutputHelper output) =>
        this.output = output;

    [PositionListPerformanceFact]
    public async Task Position_list_queries_have_runtime_postgresql_evidence()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ITS_RUN_POSITION_LIST_PERFORMANCE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var fixture = new PositionListPerformanceFixture();
        await fixture.StartAsync();
        var userId = UserId.FromGuid(await FindUserAsync(fixture.ConnectionString, 0));
        var accountId = ExchangeAccountId.FromGuid(await FindAccountAsync(fixture.ConnectionString, userId.Value));
        await using var context = fixture.CreateContext();
        var capture = new PositionListCommandCapture();
        await using var capturedContext = fixture.CreateContext(capture);
        var repository = new PositionReadRepository(capturedContext);

        var scenarios = new[]
        {
            new Scenario("default-first", PositionReadQuery.Create(null, null, null, null, 50, null)),
            new Scenario("account-first", PositionReadQuery.Create(accountId, null, null, null, 50, null)),
            new Scenario("closed-first", PositionReadQuery.Create(null, PositionTrackingState.Closed, null, null, 50, null)),
            new Scenario("symbol-working", PositionReadQuery.Create(null, null, "symbol01", null, 50, null)),
            new Scenario("symbol-closed", PositionReadQuery.Create(null, PositionTrackingState.Closed, "SYMBOL01", null, 50, null)),
            new Scenario("explicit-state-side", PositionReadQuery.Create(null, PositionTrackingState.Active, null, PositionSide.Long, 50, null)),
            new Scenario("composed", PositionReadQuery.Create(accountId, PositionTrackingState.Stale, "SyMbOl02", PositionSide.Short, 50, null)),
        };

        foreach (var scenario in scenarios)
        {
            await MeasureScenarioAsync(output, repository, capturedContext, capture, userId, scenario);
        }

        var defaultPage = await repository.ListAsync(
            userId,
            PositionReadQuery.Create(null, null, null, null, 50, null));
        var deepCursor = await FindCursorAsync(
            fixture.ConnectionString,
            userId.Value,
            null,
            2_000,
            "Active", "Unknown", "Stale");
        await MeasureScenarioAsync(
            output,
            repository,
            capturedContext,
            capture,
            userId,
            new Scenario("default-deep-cursor",
                PositionReadQuery.Create(null, null, null, null, 50, deepCursor)));

        await MeasureScenarioAsync(
            output,
            repository,
            capturedContext,
            capture,
            userId,
            new Scenario("multi-account-deep-cursor",
                PositionReadQuery.Create(null, null, null, null, 50, deepCursor)));

        var accountCursor = await FindCursorAsync(
            fixture.ConnectionString,
            userId.Value,
            accountId.Value,
            500,
            "Active", "Unknown", "Stale");
        await MeasureScenarioAsync(
            output,
            repository,
            capturedContext,
            capture,
            userId,
            new Scenario("account-deep-cursor",
                PositionReadQuery.Create(accountId, null, null, null, 50, accountCursor)));

        var closedCursor = await FindCursorAsync(
            fixture.ConnectionString,
            userId.Value,
            null,
            20_000,
            "Closed");
        await MeasureScenarioAsync(
            output,
            repository,
            capturedContext,
            capture,
            userId,
            new Scenario("closed-deep-cursor",
                PositionReadQuery.Create(null, PositionTrackingState.Closed, null, null, 50, closedCursor)));

        await using var accountCountProbe = new PositionListPerformanceFixture();
        await accountCountProbe.StartAsync(accountsPerUser: 12);
        var probeUserId = UserId.FromGuid(await FindUserAsync(
            accountCountProbe.ConnectionString,
            0));
        var probeCursor = await FindCursorAsync(
            accountCountProbe.ConnectionString,
            probeUserId.Value,
            null,
            2_000,
            "Active", "Unknown", "Stale");
        await using var probeContext = accountCountProbe.CreateContext();
        var probeCapture = new PositionListCommandCapture();
        await using var probeCapturedContext = accountCountProbe.CreateContext(probeCapture);
        await MeasureScenarioAsync(
            output,
            new PositionReadRepository(probeCapturedContext),
            probeCapturedContext,
            probeCapture,
            probeUserId,
            new Scenario("account-count-probe-12",
                PositionReadQuery.Create(null, null, null, null, 50, probeCursor)));

        Assert.NotEmpty(defaultPage.Items);
    }

    private static async Task MeasureScenarioAsync(
        ITestOutputHelper output,
        PositionReadRepository repository,
        TradeSystemDbContext context,
        PositionListCommandCapture capture,
        UserId userId,
        Scenario scenario)
    {
        capture.Commands.Clear();
        await repository.ListAsync(userId, scenario.Query);
        var commands = capture.Commands.ToArray();
        if (commands.Length == 0)
        {
            throw new InvalidOperationException($"No EF command captured for {scenario.Name}.");
        }

        var executions = new List<PlanSummary>[commands.Length];
        for (var commandIndex = 0; commandIndex < commands.Length; commandIndex++)
        {
            executions[commandIndex] = [];
            for (var run = 0; run < 4; run++)
            {
                var plan = await ExplainAsync(context, commands[commandIndex]);
                if (run > 0)
                {
                    executions[commandIndex].Add(ParsePlan(plan));
                }
            }
        }

        var medians = executions
            .Select(items => items.OrderBy(item => item.ExecutionTimeMs).ElementAt(items.Count / 2))
            .ToArray();
        var median = medians
            .OrderBy(item => item.ExecutionTimeMs)
            .ElementAt(medians.Length / 2);
        output.WriteLine(
            $"POSITION_LIST_BENCHMARK scenario={scenario.Name} round_trips={commands.Length} " +
            $"planning_ms={median.PlanningTimeMs:F2} median_ms={median.ExecutionTimeMs:F2} rows={median.ActualRows} " +
            $"removed={median.RowsRemovedByFilter} buffers={median.Buffers} " +
            $"nodes={string.Join('|', median.Nodes)} indexes={string.Join('|', median.Indexes)} " +
            $"sorts={string.Join('|', median.Sorts)}");
        for (var index = 0; index < commands.Length; index++)
        {
            output.WriteLine(
                $"POSITION_LIST_ACCOUNT_PLAN scenario={scenario.Name} index={index} " +
                $"median_ms={medians[index].ExecutionTimeMs:F2} rows={medians[index].ActualRows} " +
                $"buffers={medians[index].Buffers} indexes={string.Join('|', medians[index].Indexes)}");
        }
    }

    private static async Task<string> ExplainAsync(
        TradeSystemDbContext context,
        CapturedCommand command)
    {
        await using var dbCommand = context.Database.GetDbConnection().CreateCommand();
        dbCommand.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command.CommandText;
        foreach (var parameter in command.Parameters)
        {
            dbCommand.Parameters.Add((NpgsqlParameter)parameter.Clone());
        }

        if (dbCommand.Connection!.State != System.Data.ConnectionState.Open)
        {
            await dbCommand.Connection.OpenAsync();
        }

        return (string)(await dbCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PostgreSQL returned an empty EXPLAIN result."));
    }

    private static PlanSummary ParsePlan(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement[0];
        var plan = root.GetProperty("Plan");
        var nodes = new List<string>();
        var indexes = new List<string>();
        var sorts = new List<string>();
        var actualRows = 0d;
        var removed = 0d;
        var sharedHitBlocks = 0;
        var sharedReadBlocks = 0;
        Visit(plan);
        return new(
            root.GetProperty("Execution Time").GetDouble(),
            actualRows,
            removed,
            root.TryGetProperty("Planning Time", out var planning) ? planning.GetDouble() : 0,
            nodes,
            indexes,
            sorts,
            Buffers: $"hit={sharedHitBlocks},read={sharedReadBlocks}");

        void Visit(JsonElement node)
        {
            if (node.TryGetProperty("Node Type", out var nodeType))
            {
                nodes.Add(nodeType.GetString() ?? "unknown");
            }

            if (node.TryGetProperty("Index Name", out var indexName))
            {
                indexes.Add(indexName.GetString() ?? "unknown");
            }

            if (node.TryGetProperty("Actual Rows", out var rows))
            {
                actualRows += rows.GetDouble();
            }

            if (node.TryGetProperty("Rows Removed by Filter", out var filtered))
            {
                removed += filtered.GetDouble();
            }

            if (node.TryGetProperty("Sort Method", out var sortMethod))
            {
                sorts.Add(sortMethod.GetString() ?? "unknown");
            }

            if (node.TryGetProperty("Shared Hit Blocks", out var hitBlocks))
            {
                sharedHitBlocks += hitBlocks.GetInt32();
            }

            if (node.TryGetProperty("Shared Read Blocks", out var readBlocks))
            {
                sharedReadBlocks += readBlocks.GetInt32();
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

    private static async Task<Guid> FindUserAsync(string connectionString, int userIndex)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT user_id FROM exchange_accounts ORDER BY user_id LIMIT 1 OFFSET @offset";
        command.Parameters.AddWithValue("offset", userIndex);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Seed user was not found."));
    }

    private static async Task<Guid> FindAccountAsync(string connectionString, Guid userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT exchange_account_id FROM exchange_accounts WHERE user_id = @user_id ORDER BY exchange_account_id LIMIT 1";
        command.Parameters.AddWithValue("user_id", userId);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Seed account was not found."));
    }

    private static async Task<PositionReadCursor> FindCursorAsync(
        string connectionString,
        Guid userId,
        Guid? accountId,
        int offset,
        params string[] states)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.first_detected_at, p.position_id
            FROM positions p
            INNER JOIN exchange_accounts a ON a.exchange_account_id = p.exchange_account_id
            WHERE a.user_id = @user_id
              AND (@account_id IS NULL OR p.exchange_account_id = @account_id)
              AND p.tracking_state = ANY(@states)
            ORDER BY p.first_detected_at DESC, p.position_id DESC
            OFFSET @offset LIMIT 1
            """;
        command.Parameters.AddWithValue("user_id", NpgsqlTypes.NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue(
            "account_id",
            NpgsqlTypes.NpgsqlDbType.Uuid,
            (object?)accountId ?? DBNull.Value);
        command.Parameters.AddWithValue("states", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, states);
        command.Parameters.AddWithValue("offset", offset);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Seed cursor anchor was not found.");
        }

        return new PositionReadCursor(reader.GetFieldValue<DateTimeOffset>(0), PositionId.FromGuid(reader.GetGuid(1)));
    }

    private sealed record Scenario(string Name, PositionReadQuery Query);

    private sealed record PlanSummary(
        double ExecutionTimeMs,
        double ActualRows,
        double RowsRemovedByFilter,
        double PlanningTimeMs,
        IReadOnlyList<string> Nodes,
        IReadOnlyList<string> Indexes,
        IReadOnlyList<string> Sorts,
        string Buffers);

    private sealed class PositionListCommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("positions", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(CapturedCommand.Create(command));
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed record CapturedCommand(string CommandText, IReadOnlyList<NpgsqlParameter> Parameters)
    {
        public static CapturedCommand Create(DbCommand command) =>
            new(
                command.CommandText,
                command.Parameters.Cast<NpgsqlParameter>()
                    .Select(parameter => parameter.Clone())
                    .ToArray());
    }
}

internal sealed class PositionListPerformanceFactAttribute : FactAttribute
{
    public PositionListPerformanceFactAttribute()
    {
        DisplayName = "Position list performance benchmark";
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ITS_RUN_POSITION_LIST_PERFORMANCE"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set ITS_RUN_POSITION_LIST_PERFORMANCE=1 to run the 250,000-row PostgreSQL benchmark.";
        }
    }
}
