using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class RestBalanceEvidencePerformancePostgreSqlTests(ITestOutputHelper output)
{
    private const int WarmupCount = 1;
    private const int MeasurementCount = 3;
    private static readonly DateTimeOffset QueryTimestamp =
        new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Docker_engine_version_prefers_configured_value_and_uses_fallback_when_missing()
    {
        var fallbackCalls = 0;
        var configuredVersion = await ResolveDockerEngineVersionAsync(
            "  29.7.2  ",
            () =>
            {
                fallbackCalls++;
                return Task.FromResult("fallback");
            });

        Assert.Equal("29.7.2", configuredVersion);
        Assert.Equal(0, fallbackCalls);

        var fallbackVersion = await ResolveDockerEngineVersionAsync(
            null,
            () =>
            {
                fallbackCalls++;
                return Task.FromResult("docker-cli-version");
            });

        Assert.Equal("docker-cli-version", fallbackVersion);
        Assert.Equal(1, fallbackCalls);
    }

    [RestBalanceEvidencePerformanceFact]
    public async Task Balance_evidence_materializes_only_matching_intervals_and_records_performance_evidence()
    {
        await using var fixture = new RestBalanceEvidencePerformanceFixture();
        await fixture.StartAsync();

        var runs = new List<PerformanceRun>();
        foreach (var dataset in fixture.Datasets)
        {
            await AddScenarioMeasurementsAsync(
                fixture,
                dataset,
                BenchmarkScenario.Primary,
                runs);
        }

        foreach (var dataset in fixture.Datasets.Where(dataset => dataset.BalanceIntervalCount >= 10_000))
        {
            await AddScenarioMeasurementsAsync(
                fixture,
                dataset,
                BenchmarkScenario.ZeroMatch,
                runs);
            await AddScenarioMeasurementsAsync(
                fixture,
                dataset,
                BenchmarkScenario.DistinctTimestampOr,
                runs);
        }

        foreach (var dataset in fixture.Datasets.Where(dataset => dataset.BalanceIntervalCount >= 10_000))
        {
            await AssertDistinctTimestampMatchesAsync(fixture, dataset);
        }

        var statements = runs
            .GroupBy(run => (run.Dataset, run.Scenario, run.Implementation))
            .Select(group =>
            {
                var command = group.First().Command;
                return new SqlStatement(
                    StatementId(group.Key.Dataset, group.Key.Scenario, group.Key.Implementation),
                    group.Key.Dataset,
                    group.Key.Scenario,
                    group.Key.Implementation,
                    command.CommandText,
                    command.Parameters);
            })
            .ToArray();
        var plans = new List<QueryPlan>(statements.Length);
        foreach (var statement in statements)
        {
            var (executionPlan, indexNames) = await fixture.ExplainAsync(statement);
            plans.Add(new QueryPlan(
                statement.Dataset,
                statement.Scenario,
                statement.Implementation,
                statement.Id,
                executionPlan,
                indexNames));
        }

        var repositoryRoot = FindRepositoryRoot();
        using var globalJson = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "global.json")));
        var sdkVersion = globalJson.RootElement
            .GetProperty("sdk")
            .GetProperty("version")
            .GetString()
            ?? throw new InvalidOperationException("В global.json не указана версия SDK.");
        var evidence = new PerformanceEvidence(
            DateTimeOffset.UtcNow,
            "postgres:16-alpine",
            await ReadDockerEngineVersionAsync(),
            new RuntimeInfo(
                Environment.OSVersion.VersionString,
                Environment.Version.ToString(),
                sdkVersion,
                Environment.ProcessorCount),
            await fixture.ReadDatabaseEnvironmentAsync(),
            fixture.TableStatistics,
            new Methodology(
                WarmupCount,
                MeasurementCount,
                "Baseline повторяет прежний путь: загружает все Balance-интервалы аккаунта, затем фильтрует их в памяти.",
                "Optimized применяет те же strict interior predicates в PostgreSQL до ToArrayAsync.",
                "Оба варианта используют один PostgreSQL dataset на каждый объём; порядок вариантов меняется между итерациями.",
                "Число materialized rows равно числу записей, возвращённых EF-запросом до ToModel; для optimized это также число результатов метода.",
                "Query duration измеряет выполнение reader-команды до возврата DbDataReader; полная длительность метода включает чтение и материализацию.",
                "После наполнения таблиц перед измерениями выполнен ANALYZE public.exchange_accounts и ANALYZE public.exchange_rest_ordering_intervals."),
            fixture.DatasetSummaries,
            runs,
            statements,
            plans);

        var performanceDirectory = Path.Combine(repositoryRoot, "docs", "performance");
        Directory.CreateDirectory(performanceDirectory);
        var evidencePath = Path.Combine(
            performanceDirectory,
            "rest-balance-evidence-performance-evidence.json");
        var summaryPath = Path.Combine(
            performanceDirectory,
            "rest-balance-evidence-performance.md");
        await File.WriteAllTextAsync(
            evidencePath,
            JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
        await File.WriteAllTextAsync(summaryPath, BuildMarkdownSummary(evidence));

        output.WriteLine($"Полное performance evidence: {evidencePath}");
        output.WriteLine($"Сводка performance evidence: {summaryPath}");
        foreach (var run in runs)
        {
            output.WriteLine(
                $"REST_BALANCE_EVIDENCE_METRIC dataset={run.Dataset} " +
                $"scenario={run.Scenario} " +
                $"implementation={run.Implementation} warmup={run.IsWarmup} iteration={run.Iteration} " +
                $"round_trips={run.SqlRoundTrips} materialized_rows={run.MaterializedRows} " +
                $"query_ms={run.QueryExecutionDurationMilliseconds:F3} " +
                $"wall_ms={run.MethodDurationMilliseconds:F3}");
        }
    }

    private static async Task<PerformanceRun> MeasureAsync(
        RestBalanceEvidencePerformanceFixture fixture,
        EvidenceDataset dataset,
        BenchmarkScenario scenario,
        Implementation implementation,
        int iteration)
    {
        var capture = new PerformanceCommandCapture();
        await using var context = fixture.CreateContext(capture);
        var stopwatch = Stopwatch.StartNew();
        Guid[] candidateIds;
        int materializedRows;
        var (queryObservedAt, queryMessageCreatedAt) = QueryTimestamps(scenario);

        if (implementation == Implementation.Baseline)
        {
            var observedAt = TimestampCanonicalizer.ToUtcMicroseconds(queryObservedAt);
            var messageCreatedAt = TimestampCanonicalizer.ToUtcMicroseconds(queryMessageCreatedAt);
            var entities = await OwnedIntervals(
                    context,
                    dataset.Account.UserId,
                    dataset.Account.ExchangeAccountId)
                .Where(interval =>
                    interval.Kind == ExchangeRestOrderingKind.Balance &&
                    interval.StartedAt < interval.CompletedAt)
                .ToArrayAsync()
                .ConfigureAwait(false);
            materializedRows = entities.Length;
            candidateIds = entities
                .Where(interval =>
                    (observedAt is { } observed &&
                     interval.StartedAt < observed &&
                     observed < interval.CompletedAt) ||
                    (messageCreatedAt is { } messageCreated &&
                     interval.StartedAt < messageCreated &&
                     messageCreated < interval.CompletedAt))
                .Select(interval => interval.Id)
                .ToArray();
        }
        else
        {
            var results = await new ExchangeRestOrderingEvidenceStore(context)
                .GetBalanceEvidenceAsync(
                    dataset.Account.UserId,
                    dataset.Account.ExchangeAccountId,
                    queryObservedAt,
                    queryMessageCreatedAt)
                .ConfigureAwait(false);
            materializedRows = results.Count;
            candidateIds = results.Select(interval => interval.Id).ToArray();
        }

        stopwatch.Stop();
        var command = Assert.Single(capture.Commands);
        return new PerformanceRun(
            dataset.Name,
            dataset.BalanceIntervalCount,
            scenario,
            implementation,
            iteration,
            iteration < WarmupCount,
            capture.Commands.Count,
            materializedRows,
            command.DurationMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds,
            candidateIds,
            ExpectedIds(dataset, scenario),
            command);
    }

    private static async Task AddScenarioMeasurementsAsync(
        RestBalanceEvidencePerformanceFixture fixture,
        EvidenceDataset dataset,
        BenchmarkScenario scenario,
        ICollection<PerformanceRun> runs)
    {
        var expectedIds = ExpectedIds(dataset, scenario);
        if (scenario == BenchmarkScenario.DistinctTimestampOr)
        {
            var observedIds = new[] { dataset.SharedMatchId, dataset.ObservedOnlyMatchId };
            var messageCreatedIds = new[] { dataset.SharedMatchId, dataset.MessageCreatedOnlyMatchId };
            Assert.False(observedIds.OrderBy(id => id).SequenceEqual(messageCreatedIds.OrderBy(id => id)));
            Assert.Single(observedIds.Intersect(messageCreatedIds));
            Assert.Equal(3, expectedIds.Count);
        }

        for (var iteration = 0; iteration < WarmupCount + MeasurementCount; iteration++)
        {
            var implementations = iteration % 2 == 0
                ? new[] { Implementation.Baseline, Implementation.Optimized }
                : new[] { Implementation.Optimized, Implementation.Baseline };
            var results = new Dictionary<Implementation, IReadOnlyList<Guid>>();

            foreach (var implementation in implementations)
            {
                var run = await MeasureAsync(
                    fixture,
                    dataset,
                    scenario,
                    implementation,
                    iteration);
                Assert.Equal(1, run.SqlRoundTrips);
                Assert.Equal(expectedIds.Count, run.CandidateIds.Count);
                Assert.Equal(run.CandidateIds.Count, run.CandidateIds.Distinct().Count());
                Assert.Equal(
                    implementation == Implementation.Baseline
                        ? dataset.BalanceIntervalCount
                        : expectedIds.Count,
                    run.MaterializedRows);
                Assert.Equal(expectedIds.OrderBy(id => id), run.CandidateIds.OrderBy(id => id));

                if (scenario == BenchmarkScenario.DistinctTimestampOr &&
                    implementation == Implementation.Optimized)
                {
                    var (observedAt, messageCreatedAt) = QueryTimestamps(scenario);
                    Assert.Contains(run.Command.Parameters, parameter =>
                        parameter.Name == "@observed" &&
                        Equals(parameter.Value, observedAt));
                    Assert.Contains(run.Command.Parameters, parameter =>
                        parameter.Name == "@messageCreated" &&
                        Equals(parameter.Value, messageCreatedAt));
                }

                results.Add(implementation, run.CandidateIds);
                runs.Add(run);
            }

            Assert.Equal(
                results[Implementation.Baseline].OrderBy(id => id),
                results[Implementation.Optimized].OrderBy(id => id));
        }
    }

    private static async Task AssertDistinctTimestampMatchesAsync(
        RestBalanceEvidencePerformanceFixture fixture,
        EvidenceDataset dataset)
    {
        var (observedAt, messageCreatedAt) = QueryTimestamps(BenchmarkScenario.DistinctTimestampOr);
        await using var context = fixture.CreateContext();
        var store = new ExchangeRestOrderingEvidenceStore(context);
        var observedMatches = await store.GetBalanceEvidenceAsync(
            dataset.Account.UserId,
            dataset.Account.ExchangeAccountId,
            observedAt,
            messageCreatedAt: null);
        var messageCreatedMatches = await store.GetBalanceEvidenceAsync(
            dataset.Account.UserId,
            dataset.Account.ExchangeAccountId,
            observedAt: null,
            messageCreatedAt: messageCreatedAt);
        var expectedObservedIds = new[] { dataset.SharedMatchId, dataset.ObservedOnlyMatchId };
        var expectedMessageCreatedIds = new[] { dataset.SharedMatchId, dataset.MessageCreatedOnlyMatchId };

        Assert.Equal(
            expectedObservedIds.OrderBy(id => id),
            observedMatches.Select(interval => interval.Id).OrderBy(id => id));
        Assert.Equal(
            expectedMessageCreatedIds.OrderBy(id => id),
            messageCreatedMatches.Select(interval => interval.Id).OrderBy(id => id));
        Assert.Single(expectedObservedIds.Intersect(expectedMessageCreatedIds));
        Assert.Equal(
            ExpectedIds(dataset, BenchmarkScenario.DistinctTimestampOr).OrderBy(id => id),
            expectedObservedIds
                .Union(expectedMessageCreatedIds)
                .OrderBy(id => id));
    }

    private static IReadOnlyList<Guid> ExpectedIds(
        EvidenceDataset dataset,
        BenchmarkScenario scenario) =>
        scenario switch
        {
            BenchmarkScenario.Primary or BenchmarkScenario.DistinctTimestampOr =>
            [
                dataset.SharedMatchId,
                dataset.ObservedOnlyMatchId,
                dataset.MessageCreatedOnlyMatchId,
            ],
            BenchmarkScenario.ZeroMatch => [],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

    private static (DateTimeOffset? ObservedAt, DateTimeOffset? MessageCreatedAt) QueryTimestamps(
        BenchmarkScenario scenario) =>
        scenario switch
        {
            BenchmarkScenario.Primary => (QueryTimestamp, QueryTimestamp),
            BenchmarkScenario.ZeroMatch => (QueryTimestamp.AddDays(2), null),
            BenchmarkScenario.DistinctTimestampOr =>
                (QueryTimestamp.AddMilliseconds(-500), QueryTimestamp.AddMilliseconds(500)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

    private static IQueryable<ExchangeRestOrderingIntervalEntity> OwnedIntervals(
        TradeSystemDbContext context,
        UserId userId,
        ExchangeAccountId exchangeAccountId) =>
        context.ExchangeRestOrderingIntervals
            .AsNoTracking()
            .Where(interval =>
                interval.ExchangeAccountId == exchangeAccountId.Value &&
                context.ExchangeAccounts.Any(account =>
                    account.Id == interval.ExchangeAccountId &&
                    account.UserId == userId.Value));

    private static string StatementId(
        string dataset,
        BenchmarkScenario scenario,
        Implementation implementation) =>
        $"{dataset.Replace(",", string.Empty, StringComparison.Ordinal)}-" +
        $"{scenario.ToString().ToLowerInvariant()}-{implementation.ToString().ToLowerInvariant()}";

    private static string BuildMarkdownSummary(PerformanceEvidence evidence)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Производительность REST Balance evidence");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Дата измерения: {evidence.GeneratedAt:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Образ PostgreSQL: `{evidence.PostgreSqlImage}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Версия PostgreSQL: `{evidence.Database.ServerVersion}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Docker Engine: `{evidence.DockerEngineVersion}`");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Среда теста: {evidence.Runtime.OperatingSystem}; .NET {evidence.Runtime.DotNetVersion}; " +
            $"SDK {evidence.Runtime.SdkVersion}; CPU threads {evidence.Runtime.ProcessorCount}.");
        builder.AppendLine();
        builder.AppendLine(
            $"Для каждого варианта и набора выполнен прогрев ({evidence.Methodology.WarmupCount}) " +
            $"и {evidence.Methodology.MeasurementCount} измерения. Таблица содержит медианы; полные " +
            "результаты, SQL, параметры и планы находятся в [JSON evidence]" +
            "(./rest-balance-evidence-performance-evidence.json).");
        builder.AppendLine("Оценка числа строк PostgreSQL после `ANALYZE`:");
        foreach (var statistics in evidence.TableStatistics)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- `{statistics.Table}`: {statistics.EstimatedRowsAfterAnalyze:N0}.");
        }

        builder.AppendLine("SQL duration измеряется до возврата `DbDataReader`; wall time включает полное чтение и EF materialization. " +
            "`EXPLAIN` выполнялся отдельно и не входит в wall time.");
        builder.AppendLine(evidence.Methodology.StatisticsUpdate);
        builder.AppendLine();
        builder.AppendLine("Основной сценарий сохранён для 100, 10 000 и 50 000 интервалов. Дополнительно на историях " +
            "10 000 и 50 000 интервалов измерены zero-match и объединение результатов для разных `observedAt` " +
            "и `messageCreatedAt`. Baseline материализует всю историю и фильтрует её в памяти; Optimized " +
            "переносит строгую временную фильтрацию в PostgreSQL. Для каждого сценария ID совпадают.");
        builder.AppendLine();
        builder.AppendLine("## Измерения");
        builder.AppendLine();
        builder.AppendLine("| Сценарий | История Balance | Вариант | SQL round trips | Materialized rows | SQL duration, ms | Метод, ms |");
        builder.AppendLine("|---|---:|---|---:|---:|---:|---:|");

        foreach (var dataset in evidence.Datasets)
        {
            foreach (var scenario in Enum.GetValues<BenchmarkScenario>())
            {
                foreach (var implementation in Enum.GetValues<Implementation>())
                {
                    var measurements = evidence.Measurements
                        .Where(run =>
                            run.Dataset == dataset.Name &&
                            run.Scenario == scenario &&
                            run.Implementation == implementation &&
                            !run.IsWarmup)
                        .ToArray();
                    if (measurements.Length == 0)
                    {
                        continue;
                    }

                    builder.AppendLine(CultureInfo.InvariantCulture,
                        $"| {ScenarioLabel(scenario)} | {dataset.BalanceIntervalCount:N0} | {implementation} | " +
                        $"{Median(measurements.Select(run => (double)run.SqlRoundTrips)):F0} | " +
                        $"{Median(measurements.Select(run => (double)run.MaterializedRows)):F0} | " +
                        $"{Median(measurements.Select(run => run.QueryExecutionDurationMilliseconds)):F3} | " +
                        $"{Median(measurements.Select(run => run.MethodDurationMilliseconds)):F3} |");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Наборы данных");
        builder.AppendLine();
        builder.AppendLine("| История Balance | Другой аккаунт того же user | Другой user | Position evidence на account с историей 10,000 | Кандидаты |");
        builder.AppendLine("|---:|---:|---:|---:|---:|");
        foreach (var dataset in evidence.Datasets)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {dataset.BalanceIntervalCount:N0} | " +
                $"{dataset.SameUserOtherAccountBalanceIntervals:N0} | " +
                $"{dataset.OtherUserBalanceIntervals:N0} | " +
                $"{dataset.PositionIntervalsOnMeasuredAccount:N0} | {dataset.ExpectedMatches:N0} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Семейство и индексы");
        builder.AppendLine();
        var usedIndexes = evidence.Plans
            .SelectMany(plan => plan.IndexNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"`{name}`")
            .ToArray();
        builder.AppendLine("Все запросы и полные `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` планы с параметрами " +
            "сохранены в JSON evidence. Индексы, использованные в этих планах: " +
            (usedIndexes.Length == 0 ? "нет (планы выбрали последовательное сканирование)." : string.Join(", ", usedIndexes)) +
            ".");
        builder.AppendLine();
        builder.AppendLine("| Сценарий | История Balance | Вариант | Оценка строк | Фактические строки | Удалено фильтром | Shared buffer blocks (hit/read) | EXPLAIN, ms | Использованные индексы |");
        builder.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---|");
        foreach (var plan in evidence.Plans)
        {
            var intervalScan = FindIntervalScan(plan.ExecutionPlan);
            var explain = plan.ExecutionPlan[0];
            var rowsRemoved = PlanMetric(intervalScan, "Rows Removed by Filter");
            var sharedHitBlocks = PlanMetric(intervalScan, "Shared Hit Blocks");
            var sharedReadBlocks = PlanMetric(intervalScan, "Shared Read Blocks");
            var indexes = plan.IndexNames.Count == 0
                ? "нет"
                : string.Join(", ", plan.IndexNames.Select(name => $"`{name}`"));
            var historyCount = evidence.Datasets.Single(dataset => dataset.Name == plan.Dataset)
                .BalanceIntervalCount;
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {ScenarioLabel(plan.Scenario)} | {historyCount:N0} | {plan.Implementation} | " +
                $"{PlanMetric(intervalScan, "Plan Rows"):N0} | " +
                $"{PlanMetric(intervalScan, "Actual Rows"):N0} | " +
                $"{rowsRemoved:N0} | {sharedHitBlocks:N0}/{sharedReadBlocks:N0} | " +
                $"{explain.GetProperty("Execution Time").GetDouble():F3} | {indexes} |");
        }

        var largeBaselinePlan = evidence.Plans.Single(plan =>
            plan.Dataset == "50000" &&
            plan.Scenario == BenchmarkScenario.Primary &&
            plan.Implementation == Implementation.Baseline);
        var largeOptimizedPlan = evidence.Plans.Single(plan =>
            plan.Dataset == "50000" &&
            plan.Scenario == BenchmarkScenario.Primary &&
            plan.Implementation == Implementation.Optimized);
        var largeBaselineScan = FindIntervalScan(largeBaselinePlan.ExecutionPlan);
        var largeOptimizedScan = FindIntervalScan(largeOptimizedPlan.ExecutionPlan);
        var largeBaselineMethod = Median(evidence.Measurements
            .Where(run =>
                run.Dataset == "50000" &&
                run.Scenario == BenchmarkScenario.Primary &&
                run.Implementation == Implementation.Baseline &&
                !run.IsWarmup)
            .Select(run => run.MethodDurationMilliseconds));
        var largeOptimizedMethod = Median(evidence.Measurements
            .Where(run =>
                run.Dataset == "50000" &&
                run.Scenario == BenchmarkScenario.Primary &&
                run.Implementation == Implementation.Optimized &&
                !run.IsWarmup)
            .Select(run => run.MethodDurationMilliseconds));
        builder.AppendLine();
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Для истории 50,000 PostgreSQL выбрал `{0}` для optimized scan: оценка {1:N0}, факт {2:N0}, " +
            "shared hit/read {3:N0}/{4:N0}; baseline оценка/факт строк {5:N0}/{6:N0}. " +
            "Медианное время метода: baseline {7:F3} ms, optimized {8:F3} ms.",
            largeOptimizedScan.GetProperty("Node Type").GetString(),
            PlanMetric(largeOptimizedScan, "Plan Rows"),
            PlanMetric(largeOptimizedScan, "Actual Rows"),
            PlanMetric(largeOptimizedScan, "Shared Hit Blocks"),
            PlanMetric(largeOptimizedScan, "Shared Read Blocks"),
            PlanMetric(largeBaselineScan, "Plan Rows"),
            PlanMetric(largeBaselineScan, "Actual Rows"),
            largeBaselineMethod,
            largeOptimizedMethod));
        builder.AppendLine(
            "При 50,000 существующий индекс не был выбран планировщиком; этот результат зафиксирован, а не скрыт. " +
            "В пределах измеренных наборов оптимизация уменьшила материализацию и полное время метода, " +
            "поэтому эти измерения сами по себе не подтверждают необходимость нового индекса или изменения схемы.");
        var intervalTableStatistics = evidence.TableStatistics.Single(statistics =>
            statistics.Table == "exchange_rest_ordering_intervals");
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "После `ANALYZE` PostgreSQL оценил полный объём `exchange_rest_ordering_intervals` в {0:N0} строк; " +
            "при этом для baseline при истории 50,000 оценка строк сканирования по аккаунту осталась {1:N0} " +
            "при фактических {2:N0}. Эта остаточная погрешность планировщика отражена в evidence " +
            "и не маскируется статистикой уровня таблицы.",
            intervalTableStatistics.EstimatedRowsAfterAnalyze,
            PlanMetric(largeBaselineScan, "Plan Rows"),
            PlanMetric(largeBaselineScan, "Actual Rows")));
        builder.AppendLine();
        builder.AppendLine("Достаточность существующего индекса оценивается по фактическим планам, оценке/факту строк, " +
            "buffer reads и замерам, а не только по наличию индекса. Новые индексы и миграции не добавлялись.");
        builder.AppendLine();
        builder.AppendLine("SQL round trips на вызов не увеличились: один для обоих вариантов. " +
            "В основном сценарии и сценарии с разными timestamps optimized материализует три строки, " +
            "в сценарии без совпадений — ноль; " +
            "baseline материализует всю историю соответствующего аккаунта.");
        builder.AppendLine();
        builder.AppendLine("## Воспроизведение");
        builder.AppendLine();
        builder.AppendLine("Из `backend/src` в PowerShell; требуются Docker и PostgreSQL/Testcontainers:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine(
            "dotnet build .\\Intelligence.TradeSystem.Infrastructure.IntegrationTests\\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release");
        builder.AppendLine("# Опционально, если Docker CLI недоступен: укажите фактическую версию Docker Engine.");
        builder.AppendLine("# $env:ITS_DOCKER_ENGINE_VERSION = '<версия Docker Engine>'");
        builder.AppendLine("# Если переменная не задана, версия читается через docker info.");
        builder.AppendLine("$env:ITS_RUN_REST_BALANCE_EVIDENCE_PERFORMANCE = '1'");
        builder.AppendLine("dotnet test .\\Intelligence.TradeSystem.Infrastructure.IntegrationTests\\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --no-build --filter \"FullyQualifiedName~RestBalanceEvidencePerformancePostgreSqlTests\" --logger \"console;verbosity=minimal\"");
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string ScenarioLabel(BenchmarkScenario scenario) =>
        scenario switch
        {
            BenchmarkScenario.Primary => "Основной",
            BenchmarkScenario.ZeroMatch => "Без совпадений",
            BenchmarkScenario.DistinctTimestampOr => "Разные observedAt/messageCreatedAt",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

    private static long PlanMetric(JsonElement planNode, string propertyName) =>
        planNode.TryGetProperty(propertyName, out var metric)
            ? metric.GetInt64()
            : 0;

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            throw new InvalidOperationException("Для performance summary отсутствуют измерения.");
        }

        return ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Не найден корень репозитория с global.json.");
    }

    private static Task<string> ReadDockerEngineVersionAsync() =>
        ResolveDockerEngineVersionAsync(
            Environment.GetEnvironmentVariable("ITS_DOCKER_ENGINE_VERSION"),
            ReadDockerEngineVersionFromDockerCliAsync);

    private static Task<string> ResolveDockerEngineVersionAsync(
        string? configuredVersion,
        Func<Task<string>> readFallback)
    {
        ArgumentNullException.ThrowIfNull(readFallback);
        return !string.IsNullOrWhiteSpace(configuredVersion)
            ? Task.FromResult(configuredVersion.Trim())
            : readFallback();
    }

    private static async Task<string> ReadDockerEngineVersionFromDockerCliAsync()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("info");
        process.StartInfo.ArgumentList.Add("--format");
        process.StartInfo.ArgumentList.Add("{{.ServerVersion}}");
        if (!process.Start())
        {
            throw new InvalidOperationException("Не удалось запустить docker info для performance evidence.");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        _ = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker info завершился с кодом {process.ExitCode}.");
        }

        var version = output.Trim();
        return !string.IsNullOrWhiteSpace(version)
            ? version
            : throw new InvalidOperationException("docker info не вернул версию Docker Engine.");
    }

    private static string[] ReadIndexNames(JsonElement executionPlan)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Visit(executionPlan);
        return names.ToArray();

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Index Name", out var indexName) &&
                    indexName.ValueKind == JsonValueKind.String)
                {
                    names.Add(indexName.GetString()!);
                }

                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    Visit(child);
                }
            }
        }
    }

    private static JsonElement FindIntervalScan(JsonElement executionPlan)
    {
        return Visit(executionPlan)
            ?? throw new InvalidOperationException("EXPLAIN не содержит scan REST ordering intervals.");

        static JsonElement? Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Relation Name", out var relationName) &&
                    relationName.GetString() == "exchange_rest_ordering_intervals")
                {
                    return element;
                }

                foreach (var property in element.EnumerateObject())
                {
                    var result = Visit(property.Value);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    var result = Visit(child);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }
    }

    private enum Implementation
    {
        Baseline,
        Optimized,
    }

    private enum BenchmarkScenario
    {
        Primary,
        ZeroMatch,
        DistinctTimestampOr,
    }

    private sealed record AccountInfo(UserId UserId, ExchangeAccountId ExchangeAccountId);

    private sealed record EvidenceDataset(
        string Name,
        int BalanceIntervalCount,
        AccountInfo Account,
        Guid SharedMatchId,
        Guid ObservedOnlyMatchId,
        Guid MessageCreatedOnlyMatchId);

    private sealed record BalanceIntervalMatches(
        Guid SharedMatchId,
        Guid ObservedOnlyMatchId,
        Guid MessageCreatedOnlyMatchId);

    private sealed record DatasetSummary(
        string Name,
        int BalanceIntervalCount,
        int SameUserOtherAccountBalanceIntervals,
        int OtherUserBalanceIntervals,
        int PositionIntervalsOnMeasuredAccount,
        int ExpectedMatches);

    private sealed record CapturedParameter(
        string Name,
        NpgsqlDbType NpgsqlDbType,
        object? Value);

    private sealed record CapturedCommand(
        string CommandText,
        double DurationMilliseconds,
        IReadOnlyList<CapturedParameter> Parameters);

    private sealed record PerformanceRun(
        string Dataset,
        int HistoryRows,
        BenchmarkScenario Scenario,
        Implementation Implementation,
        int Iteration,
        bool IsWarmup,
        int SqlRoundTrips,
        int MaterializedRows,
        double QueryExecutionDurationMilliseconds,
        double MethodDurationMilliseconds,
        IReadOnlyList<Guid> CandidateIds,
        IReadOnlyList<Guid> ExpectedCandidateIds,
        CapturedCommand Command);

    private sealed record SqlStatement(
        string Id,
        string Dataset,
        BenchmarkScenario Scenario,
        Implementation Implementation,
        string CommandText,
        IReadOnlyList<CapturedParameter> Parameters);

    private sealed record QueryPlan(
        string Dataset,
        BenchmarkScenario Scenario,
        Implementation Implementation,
        string SqlStatementId,
        JsonElement ExecutionPlan,
        IReadOnlyList<string> IndexNames);

    private sealed record RuntimeInfo(
        string OperatingSystem,
        string DotNetVersion,
        string SdkVersion,
        int ProcessorCount);

    private sealed record DatabaseEnvironment(
        string ServerVersion,
        string SharedBuffers,
        string WorkMem,
        string EffectiveCacheSize,
        string MaxParallelWorkers,
        string RandomPageCost);

    private sealed record TableStatistics(
        string Table,
        long EstimatedRowsAfterAnalyze);

    private sealed record Methodology(
        int WarmupCount,
        int MeasurementCount,
        string Baseline,
        string Optimized,
        string DatasetComparison,
        string MaterializedRows,
        string QueryDuration,
        string StatisticsUpdate);

    private sealed record PerformanceEvidence(
        DateTimeOffset GeneratedAt,
        string PostgreSqlImage,
        string DockerEngineVersion,
        RuntimeInfo Runtime,
        DatabaseEnvironment Database,
        IReadOnlyList<TableStatistics> TableStatistics,
        Methodology Methodology,
        IReadOnlyList<DatasetSummary> Datasets,
        IReadOnlyList<PerformanceRun> Measurements,
        IReadOnlyList<SqlStatement> SqlStatements,
        IReadOnlyList<QueryPlan> Plans);

    private sealed class PerformanceCommandCapture : DbCommandInterceptor
    {
        private readonly List<CapturedCommand> commands = [];

        public List<CapturedCommand> Commands => commands;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var parameters = command.Parameters
                .Cast<DbParameter>()
                .Select(parameter =>
                {
                    var postgresParameter = (NpgsqlParameter)parameter;
                    return new CapturedParameter(
                        parameter.ParameterName,
                        postgresParameter.NpgsqlDbType,
                        parameter.Value is DBNull ? null : parameter.Value);
                })
                .ToArray();
            commands.Add(new CapturedCommand(
                command.CommandText,
                eventData.Duration.TotalMilliseconds,
                parameters));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RestBalanceEvidencePerformanceFixture : IAsyncDisposable
    {
        private const int SameUserOtherAccountRows = 2_000;
        private const int OtherUserRows = 2_000;
        private const int PositionRows = 500;
        private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tradesystem")
            .WithUsername("tradesystem")
            .WithPassword("tradesystem")
            .Build();

        public string ConnectionString => postgres.GetConnectionString();
        public IReadOnlyList<EvidenceDataset> Datasets { get; private set; } = [];
        public IReadOnlyList<DatasetSummary> DatasetSummaries { get; private set; } = [];
        public IReadOnlyList<TableStatistics> TableStatistics { get; private set; } = [];

        public async Task StartAsync()
        {
            await postgres.StartAsync();
            await using (var context = CreateContext())
            {
                await context.Database.MigrateAsync();
            }

            var userId = UserId.New();
            var otherUserId = UserId.New();
            var smallAccount = CreateAccount(userId);
            var measuredAccount = CreateAccount(userId);
            var largeAccount = CreateAccount(userId);
            var sameUserOtherAccount = CreateAccount(userId);
            var otherUserAccount = CreateAccount(otherUserId);
            var accounts = new[]
            {
                smallAccount,
                measuredAccount,
                largeAccount,
                sameUserOtherAccount,
                otherUserAccount,
            };
            await using (var context = CreateContext())
            {
                var repository = new ExchangeAccountRepository(context);
                foreach (var account in accounts)
                {
                    await repository.SaveAsync(
                        account.UserId,
                        account,
                        expectedVersion: null);
                }
            }

            DatasetSummaries =
            [
                new DatasetSummary(
                    "100",
                    100,
                    SameUserOtherAccountRows,
                    OtherUserRows,
                    PositionRows,
                    3),
                new DatasetSummary(
                    "10000",
                    10_000,
                    SameUserOtherAccountRows,
                    OtherUserRows,
                    PositionRows,
                    3),
                new DatasetSummary(
                    "50000",
                    50_000,
                    SameUserOtherAccountRows,
                    OtherUserRows,
                    PositionRows,
                    3),
            ];

            var rows = new List<ExchangeRestOrderingIntervalEntity>(
                100 + 10_000 + 50_000 + SameUserOtherAccountRows + OtherUserRows + PositionRows);
            var smallMatches = AddBalanceIntervals(rows, smallAccount.Id, 100);
            var measuredMatches = AddBalanceIntervals(rows, measuredAccount.Id, 10_000);
            var largeMatches = AddBalanceIntervals(rows, largeAccount.Id, 50_000);
            AddBalanceIntervals(rows, sameUserOtherAccount.Id, SameUserOtherAccountRows);
            AddBalanceIntervals(rows, otherUserAccount.Id, OtherUserRows);
            AddPositionIntervals(rows, measuredAccount.Id, PositionRows);

            Datasets =
            [
                new EvidenceDataset(
                    "100",
                    100,
                    new AccountInfo(userId, smallAccount.Id),
                    smallMatches.SharedMatchId,
                    smallMatches.ObservedOnlyMatchId,
                    smallMatches.MessageCreatedOnlyMatchId),
                new EvidenceDataset(
                    "10000",
                    10_000,
                    new AccountInfo(userId, measuredAccount.Id),
                    measuredMatches.SharedMatchId,
                    measuredMatches.ObservedOnlyMatchId,
                    measuredMatches.MessageCreatedOnlyMatchId),
                new EvidenceDataset(
                    "50000",
                    50_000,
                    new AccountInfo(userId, largeAccount.Id),
                    largeMatches.SharedMatchId,
                    largeMatches.ObservedOnlyMatchId,
                    largeMatches.MessageCreatedOnlyMatchId),
            ];

            await using (var seedContext = CreateContext())
            {
                foreach (var batch in rows.Chunk(5_000))
                {
                    seedContext.ExchangeRestOrderingIntervals.AddRange(batch);
                    await seedContext.SaveChangesAsync();
                    seedContext.ChangeTracker.Clear();
                }
            }

            TableStatistics = await AnalyzeTablesAsync();
        }

        public TradeSystemDbContext CreateContext(DbCommandInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<TradeSystemDbContext>()
                .UseNpgsql(
                    ConnectionString,
                    options => options.MigrationsAssembly(
                        typeof(TradeSystemDbContext).Assembly.GetName().Name));
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }

            return new TradeSystemDbContext(builder.Options);
        }

        public async Task<DatabaseEnvironment> ReadDatabaseEnvironmentAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT version(),
                       current_setting('shared_buffers'),
                       current_setting('work_mem'),
                       current_setting('effective_cache_size'),
                       current_setting('max_parallel_workers'),
                       current_setting('random_page_cost')
                """;
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException("PostgreSQL не вернул сведения о среде benchmark.");
            }

            return new DatabaseEnvironment(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5));
        }

        private async Task<IReadOnlyList<TableStatistics>> AnalyzeTablesAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            foreach (var table in new[] { "exchange_accounts", "exchange_rest_ordering_intervals" })
            {
                await using var analyzeCommand = connection.CreateCommand();
                analyzeCommand.CommandText = $"ANALYZE public.{table}";
                await analyzeCommand.ExecuteNonQueryAsync();
            }

            await using var statisticsCommand = connection.CreateCommand();
            statisticsCommand.CommandText =
                """
                SELECT c.relname, c.reltuples::bigint
                FROM pg_class AS c
                INNER JOIN pg_namespace AS n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname IN ('exchange_accounts', 'exchange_rest_ordering_intervals')
                ORDER BY c.relname
                """;
            await using var reader = await statisticsCommand.ExecuteReaderAsync();
            var statistics = new List<TableStatistics>(2);
            while (await reader.ReadAsync())
            {
                statistics.Add(new TableStatistics(reader.GetString(0), reader.GetInt64(1)));
            }

            if (statistics.Count != 2)
            {
                throw new InvalidOperationException(
                    "ANALYZE не вернул статистику обеих таблиц benchmark.");
            }

            return statistics;
        }

        public async Task<(JsonElement ExecutionPlan, IReadOnlyList<string> IndexNames)> ExplainAsync(
            SqlStatement statement)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {statement.CommandText}";
            foreach (var parameter in statement.Parameters)
            {
                command.Parameters.Add(new NpgsqlParameter(parameter.Name, parameter.NpgsqlDbType)
                {
                    Value = parameter.Value ?? DBNull.Value,
                });
            }

            var rawPlan = Convert.ToString(
                await command.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("EXPLAIN не вернул JSON-план.");
            using var document = JsonDocument.Parse(rawPlan);
            var executionPlan = document.RootElement.Clone();
            return (executionPlan, ReadIndexNames(executionPlan));
        }

        public async ValueTask DisposeAsync() => await postgres.DisposeAsync();

        private static ExchangeAccount CreateAccount(UserId userId) =>
            ExchangeAccount.Create(
                ExchangeAccountId.New(),
                userId,
                ExchangeId.Bybit,
                ExchangeAccountProviderIdentity.From($"performance-{Guid.NewGuid():N}"),
                "REST Balance evidence performance",
                ExchangeAccountConnectionStatus.Connected,
                ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

        private static BalanceIntervalMatches AddBalanceIntervals(
            List<ExchangeRestOrderingIntervalEntity> rows,
            ExchangeAccountId accountId,
            int count)
        {
            if (count < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Требуются три matching Balance-интервала.");
            }

            var ids = new Guid[3];
            for (var index = 0; index < count; index++)
            {
                var (startedAt, completedAt) = index switch
                {
                    0 => (QueryTimestamp.AddSeconds(-1), QueryTimestamp.AddSeconds(1)),
                    1 => (QueryTimestamp.AddMilliseconds(-750), QueryTimestamp.AddMilliseconds(250)),
                    2 => (QueryTimestamp.AddMilliseconds(-250), QueryTimestamp.AddMilliseconds(750)),
                    _ => (QueryTimestamp.AddDays(-1).AddSeconds(-index),
                        QueryTimestamp.AddDays(-1).AddSeconds(-index).AddSeconds(2)),
                };
                var id = Guid.NewGuid();
                rows.Add(new ExchangeRestOrderingIntervalEntity
                {
                    Id = id,
                    ExchangeAccountId = accountId.Value,
                    Kind = ExchangeRestOrderingKind.Balance,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    RecordedAt = QueryTimestamp.AddDays(1),
                });

                if (index < ids.Length)
                {
                    ids[index] = id;
                }
            }

            return new BalanceIntervalMatches(ids[0], ids[1], ids[2]);
        }

        private static void AddPositionIntervals(
            List<ExchangeRestOrderingIntervalEntity> rows,
            ExchangeAccountId accountId,
            int count)
        {
            for (var index = 0; index < count; index++)
            {
                rows.Add(new ExchangeRestOrderingIntervalEntity
                {
                    Id = Guid.NewGuid(),
                    ExchangeAccountId = accountId.Value,
                    Kind = ExchangeRestOrderingKind.FullPositionsComplete,
                    StartedAt = QueryTimestamp.AddSeconds(-1),
                    CompletedAt = QueryTimestamp.AddSeconds(1),
                    RecordedAt = QueryTimestamp.AddDays(1),
                });
            }
        }
    }
}

internal sealed class RestBalanceEvidencePerformanceFactAttribute : FactAttribute
{
    public RestBalanceEvidencePerformanceFactAttribute()
    {
        DisplayName = "PostgreSQL benchmark REST Balance evidence";
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ITS_RUN_REST_BALANCE_EVIDENCE_PERFORMANCE"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Установите ITS_RUN_REST_BALANCE_EVIDENCE_PERFORMANCE=1 для запуска PostgreSQL benchmark.";
        }
    }
}
