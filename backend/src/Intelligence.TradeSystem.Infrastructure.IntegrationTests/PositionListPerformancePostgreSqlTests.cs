using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Diagnostics;
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
        var userId = UserId.FromGuid(fixture.TargetUserId);
        var accountId = ExchangeAccountId.FromGuid(fixture.TargetAccountIds[0]);
        await WriteDatasetCharacteristicsAsync(output, fixture.ConnectionString);
        var capture = new PositionListCommandCapture();
        await using var capturedContext = fixture.CreateContext(capture);
        var repository = new PositionReadRepository(capturedContext);
        var composedQuery = await FindRepresentativeComposedQueryAsync(
            fixture.ConnectionString,
            userId.Value,
            fixture.TargetAccountIds);
        var workingSymbol = await FindRepresentativeSymbolAsync(
            fixture.ConnectionString,
            userId.Value,
            null);
        var closedSymbol = await FindRepresentativeSymbolAsync(
            fixture.ConnectionString,
            userId.Value,
            PositionTrackingState.Closed);

        var scenarios = new[]
        {
            new Scenario("default-first", PositionReadQuery.Create(null, null, null, null, 50, null)),
            new Scenario("account-first", PositionReadQuery.Create(accountId, null, null, null, 50, null)),
            new Scenario("closed-first", PositionReadQuery.Create(null, PositionTrackingState.Closed, null, null, 50, null)),
            new Scenario("symbol-working", PositionReadQuery.Create(null, null, workingSymbol, null, 50, null)),
            new Scenario("symbol-closed", PositionReadQuery.Create(null, PositionTrackingState.Closed, closedSymbol, null, 50, null)),
            new Scenario("explicit-state-side", PositionReadQuery.Create(null, PositionTrackingState.Active, null, PositionSide.Long, 50, null)),
            new Scenario("composed", composedQuery),
        };

        foreach (var scenario in scenarios)
        {
            await AssertScenarioIsRepresentativeAsync(
                output,
                fixture.ConnectionString,
                userId.Value,
                scenario.Query,
                scenario.Name,
                scenario.Query.PageSize + 1);
            await MeasureScenarioAsync(output, repository, capturedContext, capture, userId, scenario);
        }

        foreach (var percentile in new[] { 0.25, 0.50, 0.75, 0.95 })
        {
            var defaultCursor = await FindCursorAtPercentAsync(
                fixture.ConnectionString,
                userId.Value,
                null,
                percentile,
                "Active", "Unknown", "Stale");
            await MeasureScenarioAsync(
                output,
                repository,
                capturedContext,
                capture,
                userId,
                new Scenario(
                    $"default-deep-cursor-{percentile:P0}",
                    PositionReadQuery.Create(null, null, null, null, 50, defaultCursor)));

            var accountCursor = await FindCursorAtPercentAsync(
                fixture.ConnectionString,
                userId.Value,
                accountId.Value,
                percentile,
                "Active", "Unknown", "Stale");
            await MeasureScenarioAsync(
                output,
                repository,
                capturedContext,
                capture,
                userId,
                new Scenario(
                    $"account-deep-cursor-{percentile:P0}",
                    PositionReadQuery.Create(accountId, null, null, null, 50, accountCursor)));

            var closedCursor = await FindCursorAtPercentAsync(
                fixture.ConnectionString,
                userId.Value,
                null,
                percentile,
                "Closed");
            await MeasureScenarioAsync(
                output,
                repository,
                capturedContext,
                capture,
                userId,
                new Scenario(
                    $"closed-deep-cursor-{percentile:P0}",
                    PositionReadQuery.Create(null, PositionTrackingState.Closed, null, null, 50, closedCursor)));
        }

        await using var accountCountProbe = new PositionListPerformanceFixture();
        await accountCountProbe.StartAsync(accountsPerUser: 12);
        var probeUserId = UserId.FromGuid(accountCountProbe.TargetUserId);
        var probeCursor = await FindCursorAtPercentAsync(
            accountCountProbe.ConnectionString,
            probeUserId.Value,
            null,
            0.75,
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

        await using var singleAccountFixture = new PositionListPerformanceFixture();
        await singleAccountFixture.StartAsync(accountsPerUser: 1);
        var singleAccountUser = UserId.FromGuid(singleAccountFixture.TargetUserId);
        var singleAccountCursor = await FindCursorAtPercentAsync(
            singleAccountFixture.ConnectionString,
            singleAccountUser.Value,
            null,
            0.75,
            "Active", "Unknown", "Stale");
        var singleAccountCapture = new PositionListCommandCapture();
        await using var singleAccountContext = singleAccountFixture.CreateContext(singleAccountCapture);
        await MeasureScenarioAsync(
            output,
            new PositionReadRepository(singleAccountContext),
            singleAccountContext,
            singleAccountCapture,
            singleAccountUser,
            new Scenario(
                "single-account-deep-cursor-75%",
                PositionReadQuery.Create(null, null, null, null, 50, singleAccountCursor)));
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
        var warmupTimer = Stopwatch.StartNew();
        var warmupPage = await repository.ListAsync(userId, scenario.Query);
        warmupTimer.Stop();
        var commands = capture.Commands.ToArray();
        if (commands.Length == 0)
        {
            throw new InvalidOperationException($"No EF command captured for {scenario.Name}.");
        }

        var elapsed = new List<double>(3);
        var pages = new List<PositionReadPage>(3);
        for (var run = 0; run < 3; run++)
        {
            capture.Commands.Clear();
            var timer = Stopwatch.StartNew();
            pages.Add(await repository.ListAsync(userId, scenario.Query));
            timer.Stop();
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
            if (capture.Commands.Count != commands.Length)
            {
                throw new InvalidOperationException(
                    $"Scenario {scenario.Name} changed round-trip count between runs.");
            }
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
        var medianElapsed = elapsed.OrderBy(item => item).ElementAt(1);
        var positionPlans = medians
            .Where((_, index) =>
                commands[index].CommandText.Contains("positions", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var totalCandidates = positionPlans.Sum(plan => plan.ActualRows);
        var finalPage = pages[^1];
        output.WriteLine(
            $"POSITION_LIST_BENCHMARK scenario={scenario.Name} " +
            $"repository_elapsed_ms={medianElapsed:F2} warmup_ms={warmupTimer.Elapsed.TotalMilliseconds:F2} " +
            $"repository_round_trips={commands.Length} candidate_rows={totalCandidates:F0} " +
            $"final_merge_candidates={finalPage.Items.Count + (finalPage.HasMore ? 1 : 0)} " +
            $"planning_ms={medians.Max(item => item.PlanningTimeMs):F2} " +
            $"execution_ms={medians.Sum(item => item.ExecutionTimeMs):F2} " +
            $"rows_removed={medians.Sum(item => item.RowsRemovedByFilter):F0} " +
            $"buffers={string.Join('+', medians.Select(item => item.Buffers))} " +
            $"nodes={string.Join('|', medians.SelectMany(item => item.Nodes).Distinct())} " +
            $"indexes={string.Join('|', medians.SelectMany(item => item.Indexes).Distinct())} " +
            $"sorts={string.Join('|', medians.SelectMany(item => item.Sorts).Distinct())} " +
            $"index_conds={string.Join('|', medians.SelectMany(item => item.IndexConditions).Distinct())} " +
            $"filters={string.Join('|', medians.SelectMany(item => item.Filters).Distinct())}");
        for (var index = 0; index < commands.Length; index++)
        {
            output.WriteLine(
                $"POSITION_LIST_PLAN scenario={scenario.Name} index={index} " +
                $"median_ms={medians[index].ExecutionTimeMs:F2} rows={medians[index].ActualRows} " +
                $"loops={medians[index].ActualLoops:F0} removed={medians[index].RowsRemovedByFilter:F0} " +
                $"buffers={medians[index].Buffers} indexes={string.Join('|', medians[index].Indexes)} " +
                $"nodes={string.Join('|', medians[index].Nodes)} sorts={string.Join('|', medians[index].Sorts)} " +
                $"index_conds={string.Join('|', medians[index].IndexConditions)} " +
                $"filters={string.Join('|', medians[index].Filters)}");
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
        var indexConditions = new List<string>();
        var filters = new List<string>();
        var actualRows = plan.TryGetProperty("Actual Rows", out var rootRows)
            ? rootRows.GetDouble()
            : 0;
        var actualLoops = plan.TryGetProperty("Actual Loops", out var rootLoops)
            ? rootLoops.GetDouble()
            : 0;
        var removed = 0d;
        Visit(plan);
        return new(
            root.GetProperty("Execution Time").GetDouble(),
            actualRows,
            removed,
            root.TryGetProperty("Planning Time", out var planning) ? planning.GetDouble() : 0,
            actualLoops,
            nodes,
            indexes,
            sorts,
            indexConditions,
            filters,
            Buffers: FormatBuffers(root));

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

            if (node.TryGetProperty("Rows Removed by Filter", out var filtered))
            {
                removed += filtered.GetDouble() * GetDouble(node, "Actual Loops", 1);
            }

            if (node.TryGetProperty("Index Cond", out var indexCondition))
            {
                indexConditions.Add(indexCondition.GetString() ?? "unknown");
            }

            if (node.TryGetProperty("Filter", out var filter))
            {
                filters.Add(filter.GetString() ?? "unknown");
            }

            if (node.TryGetProperty("Sort Method", out var sortMethod))
            {
                sorts.Add(sortMethod.GetString() ?? "unknown");
            }

            if (node.TryGetProperty("Plans", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    Visit(child);
                }
            }
        }

        static double GetDouble(JsonElement node, string propertyName, double fallback = 0) =>
            node.TryGetProperty(propertyName, out var value) ? value.GetDouble() : fallback;

        static string FormatBuffers(JsonElement node) =>
            node.TryGetProperty("Shared Hit Blocks", out var hit) &&
            node.TryGetProperty("Shared Read Blocks", out var read)
                ? $"hit={hit.GetInt32()},read={read.GetInt32()}"
                : "unavailable";
    }

    private static async Task WriteDatasetCharacteristicsAsync(
        ITestOutputHelper output,
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                count(*),
                count(DISTINCT a.user_id),
                count(DISTINCT p.exchange_account_id),
                count(*) FILTER (WHERE p.tracking_state = 'Closed'),
                count(*) FILTER (WHERE p.tracking_state = 'Active'),
                count(*) FILTER (WHERE p.tracking_state = 'Unknown'),
                count(*) FILTER (WHERE p.tracking_state = 'Stale'),
                count(*) FILTER (WHERE p.position_side = 'Long'),
                count(*) FILTER (WHERE p.position_side = 'Short'),
                count(DISTINCT p.instrument_id)
            FROM positions p
            INNER JOIN exchange_accounts a ON a.exchange_account_id = p.exchange_account_id
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Dataset characteristics were not returned.");
        }

        var total = reader.GetInt64(0);
        var closed = reader.GetInt64(3);
        var closedPercentage = total == 0 ? 0 : closed * 100d / total;
        if (total != 250_000 ||
            reader.GetInt64(1) != 8 ||
            reader.GetInt64(2) != 24 ||
            closedPercentage is < 85 or > 95 ||
            reader.GetInt64(9) < 40)
        {
            throw new InvalidOperationException("Benchmark dataset characteristics are outside the approved distribution.");
        }

        output.WriteLine(
            $"POSITION_LIST_DATASET total={total} users={reader.GetInt64(1)} " +
            $"accounts={reader.GetInt64(2)} closed={closed} closed_pct={closedPercentage:F2} " +
            $"active={reader.GetInt64(4)} unknown={reader.GetInt64(5)} stale={reader.GetInt64(6)} " +
            $"long={reader.GetInt64(7)} short={reader.GetInt64(8)} symbols={reader.GetInt64(9)}");
        await reader.DisposeAsync();
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version()";
        var postgresVersion = (string)(await versionCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PostgreSQL version was not returned."));
        output.WriteLine(
            $"POSITION_LIST_ENV postgres=\"{postgresVersion}\" " +
            $"dotnet=\"{Environment.Version}\" " +
            $"efcore=\"{typeof(DbContext).Assembly.GetName().Version}\" " +
            $"npgsql=\"{typeof(NpgsqlConnection).Assembly.GetName().Version}\"");
    }

    private static async Task AssertScenarioIsRepresentativeAsync(
        ITestOutputHelper output,
        string connectionString,
        Guid userId,
        PositionReadQuery query,
        string scenario,
        long minimumCandidateCount)
    {
        var count = await CountMatchingAsync(connectionString, userId, query);
        output.WriteLine(
            $"POSITION_LIST_SCENARIO scenario={scenario} candidate_count={count} page_size={query.PageSize}");
        if (count < minimumCandidateCount)
        {
            throw new InvalidOperationException(
                $"Benchmark scenario {scenario} has {count} candidates; expected at least {minimumCandidateCount}.");
        }
    }

    private static async Task<PositionReadQuery> FindRepresentativeComposedQueryAsync(
        string connectionString,
        Guid userId,
        IReadOnlyList<Guid> accountIds)
    {
        foreach (var accountId in accountIds)
        {
            foreach (var state in new[]
                     {
                         PositionTrackingState.Active,
                         PositionTrackingState.Unknown,
                         PositionTrackingState.Stale,
                         PositionTrackingState.Closed,
                     })
            {
                foreach (var symbolIndex in Enumerable.Range(0, 40))
                {
                    foreach (var side in new[] { PositionSide.Long, PositionSide.Short })
                    {
                        var query = PositionReadQuery.Create(
                            ExchangeAccountId.FromGuid(accountId),
                            state,
                            $"SYMBOL{symbolIndex:D2}",
                            side,
                            50,
                            null);
                        if (await CountMatchingAsync(connectionString, userId, query) > query.PageSize)
                        {
                            return query;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "No representative composed benchmark combination has more than one page.");
    }

    private static async Task<string> FindRepresentativeSymbolAsync(
        string connectionString,
        Guid userId,
        PositionTrackingState? state)
    {
        foreach (var symbolIndex in Enumerable.Range(0, 40))
        {
            var query = PositionReadQuery.Create(
                null,
                state,
                $"SYMBOL{symbolIndex:D2}",
                null,
                50,
                null);
            if (await CountMatchingAsync(connectionString, userId, query) > query.PageSize)
            {
                return query.Symbol!;
            }
        }

        throw new InvalidOperationException(
            $"No representative {(state is null ? "working" : "closed")} symbol benchmark combination exists.");
    }

    private static async Task<long> CountMatchingAsync(
        string connectionString,
        Guid userId,
        PositionReadQuery query)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*)
            FROM positions p
            INNER JOIN exchange_accounts a ON a.exchange_account_id = p.exchange_account_id
            WHERE a.user_id = @user_id
              AND (@account_id IS NULL OR p.exchange_account_id = @account_id)
              AND p.tracking_state = ANY(@states)
              AND (@symbol IS NULL OR lower(p.instrument_id) = lower(@symbol))
              AND (@side IS NULL OR p.position_side = @side)
            """;
        command.Parameters.AddWithValue("user_id", NpgsqlTypes.NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue(
            "account_id",
            NpgsqlTypes.NpgsqlDbType.Uuid,
            query.ExchangeAccountId?.Value ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "states",
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text,
            query.TrackingStates.Select(state => state.ToString()).ToArray());
        command.Parameters.AddWithValue(
            "symbol",
            NpgsqlTypes.NpgsqlDbType.Text,
            query.Symbol ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "side",
            NpgsqlTypes.NpgsqlDbType.Text,
            query.Side?.ToString() ?? (object)DBNull.Value);
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Scenario count was not returned."));
    }

    private static async Task<PositionReadCursor> FindCursorAtPercentAsync(
        string connectionString,
        Guid userId,
        Guid? accountId,
        double percentile,
        params string[] states)
    {
        var count = await CountMatchingAsync(
            connectionString,
            userId,
            PositionReadQuery.Create(
                accountId is null ? null : ExchangeAccountId.FromGuid(accountId.Value),
                states.Length == 1 && states[0] == "Closed"
                    ? PositionTrackingState.Closed
                    : null,
                null,
                null,
                50,
                null));
        var offset = Math.Max(1, (int)Math.Floor((count - 1) * percentile));
        return await FindCursorAsync(connectionString, userId, accountId, offset, states);
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
        double ActualLoops,
        IReadOnlyList<string> Nodes,
        IReadOnlyList<string> Indexes,
        IReadOnlyList<string> Sorts,
        IReadOnlyList<string> IndexConditions,
        IReadOnlyList<string> Filters,
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
            Capture(command);

            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command)
        {
            if (!command.CommandText.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(CapturedCommand.Create(command));
            }
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
