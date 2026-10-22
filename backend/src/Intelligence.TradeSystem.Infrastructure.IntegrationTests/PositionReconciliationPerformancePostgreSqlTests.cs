using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Domain.Portfolio;
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

public sealed class PositionReconciliationPerformancePostgreSqlTests(ITestOutputHelper output)
{
    private const int WarmupCount = 1;
    private const int MeasurementCount = 3;
    private static readonly DateTimeOffset WorkflowAnchor =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SettlementAsset Usdt = SettlementAsset.From("USDT");
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [PositionReconciliationPerformanceFact]
    public async Task User_workflows_keep_closed_history_out_of_hot_path_measurements()
    {
        await using var fixture = new PositionReconciliationPerformanceFixture();
        await fixture.StartAsync();
        var databaseEnvironment = await fixture.ReadDatabaseEnvironmentAsync();
        var runs = new List<WorkflowRun>();

        foreach (var dataset in fixture.Datasets)
        {
            foreach (var workflow in Enum.GetValues<PositionReconciliationWorkflow>())
            {
                for (var iteration = 0; iteration < WarmupCount + MeasurementCount; iteration++)
                {
                    var implementations = iteration % 2 == 0
                        ? new[]
                        {
                            PositionReconciliationImplementation.Baseline,
                            PositionReconciliationImplementation.Optimized,
                        }
                        : new[]
                        {
                            PositionReconciliationImplementation.Optimized,
                            PositionReconciliationImplementation.Baseline,
                        };

                    foreach (var implementation in implementations)
                    {
                        var account = dataset.GetAccount(workflow, implementation);
                        runs.Add(await ExecuteWorkflowAsync(
                            fixture,
                            dataset,
                            account,
                            iteration,
                            iteration == 0));
                    }
                }
            }
        }

        AssertMeasurements(runs, fixture.Datasets);
        var plans = await ExplainMeasuredCommandsAsync(fixture.ConnectionString, runs);
        AssertPlans(plans, fixture.Datasets);

        var repositoryRoot = FindRepositoryRoot();
        var globalJson = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "global.json")));
        var sdkVersion = globalJson.RootElement
            .GetProperty("sdk")
            .GetProperty("version")
            .GetString()
            ?? throw new InvalidOperationException("В global.json не указана версия SDK.");
        var evidence = CreateEvidence(
            fixture,
            databaseEnvironment,
            sdkVersion,
            runs,
            plans);
        var evidenceDirectory = Path.Combine(repositoryRoot, "docs", "performance");
        Directory.CreateDirectory(evidenceDirectory);
        var jsonPath = Path.Combine(
            evidenceDirectory,
            "position-reconciliation-performance-evidence.json");
        var markdownPath = Path.Combine(
            evidenceDirectory,
            "position-reconciliation-performance.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
        await File.WriteAllTextAsync(markdownPath, BuildMarkdownSummary(evidence));

        output.WriteLine($"Полное performance evidence: {jsonPath}");
        output.WriteLine($"Сводка performance evidence: {markdownPath}");
        foreach (var measurement in evidence.Measurements)
        {
            output.WriteLine(
                $"POSITION_RECONCILIATION_METRIC dataset={measurement.Dataset} " +
                $"workflow={measurement.Workflow} implementation={measurement.Implementation} " +
                $"warmup={measurement.IsWarmup} iteration={measurement.Iteration} " +
                $"round_trips={measurement.SqlRoundTrips} materialized_rows={measurement.MaterializedRows.Total} " +
                $"query_ms={measurement.QueryExecutionDurationMilliseconds:F3} " +
                $"wall_ms={measurement.WallTimeMilliseconds:F3} " +
                $"position_lock_ms={measurement.PositionLockSqlDurationMilliseconds:F3} " +
                $"account_lock_ms={measurement.AccountLockSqlDurationMilliseconds:F3} " +
                $"lock_held_ms={measurement.LockHoldingDurationMilliseconds:F3} " +
                $"position_rows_locked={measurement.PositionRowsLocked}");
        }
    }

    private static async Task<WorkflowRun> ExecuteWorkflowAsync(
        PositionReconciliationPerformanceFixture fixture,
        PositionReconciliationPerformanceDataset dataset,
        PositionReconciliationPerformanceAccount account,
        int iteration,
        bool isWarmup)
    {
        var capture = new PerformanceCommandCapture();
        await using var context = fixture.CreateContext(capture);
        var materialized = new PositionMaterializationCounts();
        var userId = UserId.FromGuid(account.UserId);
        var exchangeAccountId = ExchangeAccountId.FromGuid(account.AccountId);
        var clockNow = WorkflowTime(iteration).AddSeconds(1);
        var clock = new FixedTimeProvider(clockNow);
        var baseline = account.Implementation == PositionReconciliationImplementation.Baseline;
        var positionRepository = new PerformancePositionRepository(
            new PositionRepository(context),
            account,
            baseline,
            materialized,
            capture);
        var persistenceTransaction = new PerformanceSyncTransaction(
            context,
            baseline,
            capture);
        var accountRepository = new ExchangeAccountRepository(context);
        var portfolioRepository = new PortfolioStateRepository(context);
        var outbox = new ApplicationEventOutbox(context);
        var restOrderingEvidenceStore = new ExchangeRestOrderingEvidenceStore(context);
        var walletCursorStore = new WalletObservationCursorStore(context);
        var positionCursorStore = new PositionObservationCursorStore(context);
        var observationStateStore = new RealtimeObservationStateStore(context);
        var privateProvider = new PerformancePrivateAccountProvider(
            WorkflowTime(iteration),
            CreateCurrentPositionObservations());
        var credentialStore = new PerformanceCredentialStore();
        var providerFactory = new PerformancePrivateAccountProviderFactory(privateProvider);

        using var rawCommandCapture = TradeSystemDbCommand.CaptureExecution(capture.RecordRaw);
        var wallClock = Stopwatch.StartNew();
        string outcome;
        switch (account.Workflow)
        {
            case PositionReconciliationWorkflow.WalletObservation:
            {
                var service = new RealtimeWalletObservationService(
                    accountRepository,
                    positionRepository,
                    portfolioRepository,
                    observationStateStore,
                    restOrderingEvidenceStore,
                    walletCursorStore,
                    persistenceTransaction,
                    outbox,
                    clock);
                var result = await service.ApplyAsync(
                    userId,
                    generation: 1,
                    new PrivateWalletObservation
                    {
                        Exchange = ExchangeId.Bybit,
                        ExchangeAccountId = exchangeAccountId,
                        Kind = PrivateWalletObservationKind.PartialUpdate,
                        Balances =
                        [
                            new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                        ],
                        MessageCreatedAt = WorkflowTime(iteration),
                        ReceivedAt = WorkflowTime(iteration).AddMilliseconds(50),
                    });
                Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
                outcome = result.Outcome.ToString();
                break;
            }
            case PositionReconciliationWorkflow.DirectPositionUpdate:
            case PositionReconciliationWorkflow.OpenAfterClosure:
            {
                var service = new RealtimePositionReconciliationService(
                    accountRepository,
                    credentialStore,
                    providerFactory,
                    positionRepository,
                    portfolioRepository,
                    positionCursorStore,
                    observationStateStore,
                    restOrderingEvidenceStore,
                    persistenceTransaction,
                    outbox,
                    clock);
                var positionIdx = account.Workflow == PositionReconciliationWorkflow.DirectPositionUpdate
                    ? 0
                    : 1_000 + iteration;
                var sequence = account.Workflow == PositionReconciliationWorkflow.DirectPositionUpdate
                    ? iteration + 2
                    : iteration + 1;
                var result = await service.ApplyAsync(
                    userId,
                    generation: 1,
                    CreatePositionObservation(exchangeAccountId, positionIdx, sequence, WorkflowTime(iteration)));
                Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
                outcome = result.Outcome.ToString();
                break;
            }
            case PositionReconciliationWorkflow.FullRestReconciliation:
            {
                var service = new ExchangeAccountSyncService(
                    accountRepository,
                    credentialStore,
                    providerFactory,
                    positionRepository,
                    portfolioRepository,
                    persistenceTransaction,
                    outbox,
                    restOrderingEvidenceStore,
                    walletCursorStore,
                    clock);
                var result = await service.SynchronizeAsync(userId, exchangeAccountId);
                Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, result.Outcome);
                outcome = result.Outcome.ToString();
                break;
            }
            case PositionReconciliationWorkflow.LockAcquisition:
                await persistenceTransaction.ExecuteAsync(async cancellationToken =>
                {
                    await persistenceTransaction
                        .LockPositionsAsync(userId, exchangeAccountId, cancellationToken);
                    await persistenceTransaction
                        .LockAccountAsync(userId, exchangeAccountId, cancellationToken);
                });
                outcome = "Зафиксировано";
                break;
            default:
                throw new InvalidOperationException($"Неподдерживаемый сценарий benchmark: {account.Workflow}.");
        }

        wallClock.Stop();
        var commands = capture.Commands;
        var queryExecutionDuration = commands
            .Where(IsReadCommand)
            .Sum(command => command.ExecutionDuration.TotalMilliseconds);
        var sqlCommandDuration = commands.Sum(command => command.ExecutionDuration.TotalMilliseconds);
        var measurement = new PerformanceMeasurementEvidence(
            dataset.Name,
            account.Workflow,
            account.Implementation,
            iteration,
            isWarmup,
            commands.Count,
            materialized.ToEvidence(),
            queryExecutionDuration,
            sqlCommandDuration,
            wallClock.Elapsed.TotalMilliseconds,
            persistenceTransaction.PositionRowsLocked,
            persistenceTransaction.AccountRowsLocked,
            persistenceTransaction.PositionLockSqlDuration.TotalMilliseconds,
            persistenceTransaction.AccountLockSqlDuration.TotalMilliseconds,
            persistenceTransaction.LockHoldingDuration.TotalMilliseconds,
            CommitDurationMilliseconds: null,
            persistenceTransaction.LockOrderPreserved,
            outcome,
            commands.Select(command => command.ToEvidence()).ToArray());
        return new WorkflowRun(measurement, commands);
    }

    private static PrivatePositionObservation CreatePositionObservation(
        ExchangeAccountId exchangeAccountId,
        int positionIdx,
        long sequence,
        DateTimeOffset observedAt)
    {
        var position = CreateOpenPosition(positionIdx, 100m + sequence);
        return new PrivatePositionObservation
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = exchangeAccountId,
            Kind = PrivatePositionObservationKind.Open,
            Symbol = position.Symbol,
            Category = MarketCategory.Linear,
            PositionIdx = positionIdx,
            Side = PositionSide.Long,
            Size = position.Size,
            ProviderSequence = sequence,
            ProviderUpdatedAt = observedAt,
            MessageCreatedAt = observedAt,
            ReceivedAt = observedAt.AddMilliseconds(50),
            SettlementAsset = Usdt,
            Position = position,
        };
    }

    private static OpenPosition[] CreateCurrentPositionObservations() =>
        Enumerable.Range(0, 7)
            .Select(positionIdx => CreateOpenPosition(positionIdx, markPrice: null))
            .ToArray();

    private static OpenPosition CreateOpenPosition(int positionIdx, decimal? markPrice) =>
        new(
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            PositionStatus.Normal,
            1m,
            null,
            null,
            null,
            markPrice,
            null,
            null,
            null,
            null,
            null,
            null,
            1,
            null,
            null,
            null,
            positionIdx,
            Usdt);

    private static DateTimeOffset WorkflowTime(int iteration) =>
        WorkflowAnchor.AddDays(120).AddMinutes(iteration);

    private static void AssertMeasurements(
        IReadOnlyCollection<WorkflowRun> runs,
        IReadOnlyList<PositionReconciliationPerformanceDataset> datasets)
    {
        foreach (var dataset in datasets)
        {
            foreach (var workflow in Enum.GetValues<PositionReconciliationWorkflow>())
            {
                var expectedCurrentPositions = 7;
                foreach (var implementation in Enum.GetValues<PositionReconciliationImplementation>())
                {
                    var measurements = runs
                        .Where(run =>
                            run.Measurement.Dataset == dataset.Name &&
                            run.Measurement.Workflow == workflow &&
                            run.Measurement.Implementation == implementation)
                        .Select(run => run.Measurement)
                        .OrderBy(measurement => measurement.Iteration)
                        .ToArray();
                    Assert.Equal(WarmupCount + MeasurementCount, measurements.Length);
                    Assert.Single(measurements, measurement => measurement.IsWarmup);
                    Assert.Equal(MeasurementCount, measurements.Count(measurement => !measurement.IsWarmup));

                    for (var iteration = 0; iteration < measurements.Length; iteration++)
                    {
                        var measurement = measurements[iteration];
                        var currentCount = expectedCurrentPositions +
                            (workflow == PositionReconciliationWorkflow.OpenAfterClosure ? iteration : 0);
                        var expectedLockedRows = implementation == PositionReconciliationImplementation.Baseline
                            ? dataset.ClosedLifecycleCount + currentCount
                            : currentCount;
                        Assert.Equal(expectedLockedRows, measurement.PositionRowsLocked);
                        Assert.Equal(1, measurement.AccountRowsLocked);
                        Assert.True(measurement.LockOrderPreserved);
                        Assert.True(measurement.SqlRoundTrips > 0);
                        Assert.True(measurement.QueryExecutionDurationMilliseconds >= 0);
                        Assert.True(measurement.WallTimeMilliseconds >= 0);
                        Assert.Null(measurement.CommitDurationMilliseconds);
                        if (workflow == PositionReconciliationWorkflow.WalletObservation &&
                            implementation == PositionReconciliationImplementation.Baseline &&
                            measurement.IsWarmup)
                        {
                            Assert.Equal(
                                dataset.ClosedLifecycleCount + 7,
                                measurement.MaterializedRows.Positions);
                            Assert.Equal(
                                dataset.PositionChangeCount,
                                measurement.MaterializedRows.PositionChanges);
                        }

                        if (workflow == PositionReconciliationWorkflow.DirectPositionUpdate &&
                            implementation == PositionReconciliationImplementation.Optimized)
                        {
                            Assert.Equal(6, measurement.MaterializedRows.PositionChanges);
                        }

                        if (workflow == PositionReconciliationWorkflow.FullRestReconciliation &&
                            implementation == PositionReconciliationImplementation.Optimized)
                        {
                            Assert.Equal(
                                measurement.IsWarmup ? 22 : 26,
                                measurement.MaterializedRows.PositionChanges);
                        }

                        var run = runs.Single(candidate =>
                            candidate.Measurement.Dataset == dataset.Name &&
                            candidate.Measurement.Workflow == workflow &&
                            candidate.Measurement.Implementation == implementation &&
                            candidate.Measurement.Iteration == iteration);
                        AssertLockSqlShape(run.Commands, implementation);
                    }
                }

                if (workflow != PositionReconciliationWorkflow.LockAcquisition)
                {
                    var baselineChanges = runs
                        .Where(run =>
                            run.Measurement.Dataset == dataset.Name &&
                            run.Measurement.Workflow == workflow &&
                            run.Measurement.Implementation == PositionReconciliationImplementation.Baseline &&
                            !run.Measurement.IsWarmup)
                        .Min(run => run.Measurement.MaterializedRows.PositionChanges);
                    var optimizedChanges = runs
                        .Where(run =>
                            run.Measurement.Dataset == dataset.Name &&
                            run.Measurement.Workflow == workflow &&
                            run.Measurement.Implementation == PositionReconciliationImplementation.Optimized &&
                            !run.Measurement.IsWarmup)
                        .Max(run => run.Measurement.MaterializedRows.PositionChanges);
                    Assert.True(
                        baselineChanges > optimizedChanges,
                        $"{dataset.Name}/{workflow}: история baseline содержит {baselineChanges} строк, optimized — {optimizedChanges}.");
                }
            }
        }
    }

    private static void AssertLockSqlShape(
        IReadOnlyList<CapturedSqlCommand> commands,
        PositionReconciliationImplementation implementation)
    {
        var positionLock = Assert.Single(commands, command =>
            command.CommandText.Contains("FOR UPDATE OF p", StringComparison.OrdinalIgnoreCase));
        var accountLock = Assert.Single(commands, command =>
            command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase));

        Assert.True(positionLock.Sequence < accountLock.Sequence);
        Assert.Contains("ORDER BY p.position_id", positionLock.CommandText, StringComparison.OrdinalIgnoreCase);
        if (implementation == PositionReconciliationImplementation.Baseline)
        {
            Assert.DoesNotContain("tracking_state", positionLock.CommandText, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("tracking_state <> 'Closed'", positionLock.CommandText, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<IReadOnlyList<PerformanceQueryPlanEvidence>> ExplainMeasuredCommandsAsync(
        string connectionString,
        IReadOnlyList<WorkflowRun> runs)
    {
        var plans = new List<PerformanceQueryPlanEvidence>();
        foreach (var run in runs.Where(run => !run.Measurement.IsWarmup && run.Measurement.Iteration == MeasurementCount))
        {
            var uniqueCommands = run.Commands
                .Where(IsReadCommand)
                .GroupBy(command => command.CommandText, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            foreach (var command in uniqueCommands)
            {
                var rawPlan = await ExplainAsync(connectionString, command);
                using var document = JsonDocument.Parse(rawPlan);
                var planJson = document.RootElement.Clone();
                var indexNames = GetIndexNames(planJson);
                plans.Add(new PerformanceQueryPlanEvidence(
                    run.Measurement.Dataset,
                    run.Measurement.Workflow,
                    run.Measurement.Implementation,
                    command.QueryName,
                    command.CommandText,
                    command.ToParameterEvidence(),
                    planJson,
                    indexNames,
                    IsLockingQuery(command.CommandText),
                    RollbackAfterExplain: true));
            }
        }

        return plans;
    }

    private static void AssertPlans(
        IReadOnlyList<PerformanceQueryPlanEvidence> plans,
        IReadOnlyList<PositionReconciliationPerformanceDataset> datasets)
    {
        Assert.NotEmpty(plans);
        Assert.All(plans, plan =>
        {
            Assert.NotEqual(default, plan.ExecutionPlan.ValueKind);
            Assert.True(plan.RollbackAfterExplain);
        });
        foreach (var dataset in datasets)
        {
            foreach (var workflow in Enum.GetValues<PositionReconciliationWorkflow>())
            {
                foreach (var implementation in Enum.GetValues<PositionReconciliationImplementation>())
                {
                    Assert.Contains(plans, plan =>
                        plan.Dataset == dataset.Name &&
                        plan.Workflow == workflow &&
                        plan.Implementation == implementation &&
                        plan.QueryName == "position-lock" &&
                        plan.IsLockingQuery);
                    Assert.Contains(plans, plan =>
                        plan.Dataset == dataset.Name &&
                        plan.Workflow == workflow &&
                        plan.Implementation == implementation &&
                        plan.QueryName == "account-lock" &&
                        plan.IsLockingQuery);
                }
            }
        }

        var largeDataset = datasets.Single(dataset => dataset.ClosedLifecycleCount == 10_000);
        var largeClosureLookup = Assert.Single(plans, plan =>
            plan.Dataset == largeDataset.Name &&
            plan.Workflow == PositionReconciliationWorkflow.OpenAfterClosure &&
            plan.Implementation == PositionReconciliationImplementation.Optimized &&
            plan.QueryName == "latest-closed-slots");
        Assert.Contains("ix_positions_latest_closed_slot", largeClosureLookup.IndexNames);
    }

    private static async Task<string> ExplainAsync(
        string connectionString,
        CapturedSqlCommand captured)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + captured.CommandText;
        foreach (var parameter in captured.Parameters)
        {
            command.Parameters.Add((NpgsqlParameter)parameter.Clone());
        }

        var plan = (string?)await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PostgreSQL вернул пустой результат EXPLAIN.");
        await transaction.RollbackAsync();
        return plan;
    }

    private static bool IsReadCommand(CapturedSqlCommand command)
    {
        if (!string.Equals(command.CommandType, "Reader", StringComparison.Ordinal) &&
            !string.Equals(command.CommandType, "LockReader", StringComparison.Ordinal) &&
            !string.Equals(command.CommandType, "LockScalar", StringComparison.Ordinal) &&
            !string.Equals(command.CommandType, "Scalar", StringComparison.Ordinal) &&
            !string.Equals(command.CommandType, "Raw", StringComparison.Ordinal))
        {
            return false;
        }

        var sql = command.CommandText.TrimStart();
        if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) &&
               !sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
               !sql.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
               !sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLockingQuery(string sql) =>
        sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("FOR SHARE", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("FOR KEY SHARE", StringComparison.OrdinalIgnoreCase);

    private static string[] GetIndexNames(JsonElement planJson)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Visit(planJson[0].GetProperty("Plan"));
        return names.ToArray();

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

    private static PerformanceEvidence CreateEvidence(
        PositionReconciliationPerformanceFixture fixture,
        PositionReconciliationDatabaseEnvironment databaseEnvironment,
        string sdkVersion,
        IReadOnlyList<WorkflowRun> runs,
        IReadOnlyList<PerformanceQueryPlanEvidence> plans)
    {
        var runtime = new PerformanceRuntimeEnvironment(
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            sdkVersion,
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            Environment.GetEnvironmentVariable("ITS_DOCKER_ENGINE_VERSION"));
        var datasets = fixture.Datasets
            .Select(dataset => new PerformanceDatasetEvidence(
                dataset.Name,
                dataset.ClosedLifecycleCount,
                dataset.PositionChangeCount,
                5,
                1,
                1))
            .ToArray();
        return new PerformanceEvidence(
            DateTimeOffset.UtcNow,
            "postgres:16-alpine",
            runtime,
            databaseEnvironment,
            new PerformanceMethodologyEvidence(
                WarmupCount,
                MeasurementCount,
                "Полные сценарии Application используют реальные PostgreSQL repositories, transactions, portfolio, cursor и outbox. Базовый adapter заменяет специализированные reads одним legacy GetByExchangeAccountAsync на workflow и строит projections из того же результата; остальные orchestration и write paths одинаковы. Materialized position/PositionChanges учитывают также existing history, повторно materialized приложением при append-only CAS verification.",
                "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) запускается после обычных измерений на отдельном соединении и транзакции с последующим ROLLBACK. Его время не входит в workflow measurements.",
                "Lock holding duration измеряется от начала position-lock SQL до завершения ExecuteAsync после commit. Commit не измеряется отдельно и входит в lock holding duration.",
                "Query duration для EF reader-команд заканчивается при возврате DbDataReader; полное чтение и materialization отражены в wall time и счётчиках строк. Raw-команды измеряются до завершения их reader/scalar callback.",
                "Materialized rows считает позиции, PositionChanges, portfolio projections, watermarks и результаты latest-closed lookup, включая историю, повторно загруженную для append-only CAS. Account, cursor и outbox строки сюда не входят; их SQL-команды и длительности учитываются."),
            datasets,
            runs.Select(run => run.Measurement).ToArray(),
            plans);
    }

    private static string BuildMarkdownSummary(PerformanceEvidence evidence)
    {
        var builder = new StringBuilder();
        void AppendInvariant(FormattableString line) =>
            builder.AppendLine(line.ToString(CultureInfo.InvariantCulture));

        builder.AppendLine("# Данные производительности PostgreSQL: согласование позиций");
        builder.AppendLine();
        AppendInvariant($"Дата измерения: {evidence.GeneratedAt:O}");
        AppendInvariant($"Образ PostgreSQL: `{evidence.PostgreSqlImage}`");
        AppendInvariant($"Версия PostgreSQL: `{evidence.Database.ServerVersion}`");
        AppendInvariant($"Docker Engine: `{evidence.Runtime.DockerEngineVersion ?? "не зафиксирован"}`");
        AppendInvariant(
            $"Среда теста: {evidence.Runtime.OperatingSystem}; {evidence.Runtime.Framework}; SDK {evidence.Runtime.SdkVersion}; CPU threads {evidence.Runtime.ProcessorCount}; доступная память {evidence.Runtime.AvailableMemoryBytes:N0} bytes.");
        AppendInvariant(
            $"Настройки PostgreSQL: `shared_buffers={evidence.Database.SharedBuffers}`, `work_mem={evidence.Database.WorkMem}`, `effective_cache_size={evidence.Database.EffectiveCacheSize}`, `max_parallel_workers={evidence.Database.MaxParallelWorkers}`, `random_page_cost={evidence.Database.RandomPageCost}`.");
        builder.AppendLine();
        AppendInvariant(
            $"Для каждого workflow и варианта реализации выполнен прогрев ({evidence.Methodology.WarmupCount}) и {evidence.Methodology.MeasurementCount} измерения после прогрева. В таблице приведены медианы трёх измерений; полные результаты каждого прогона и SQL приведены в JSON evidence.");
        builder.AppendLine(evidence.Methodology.MaterializedRowsDescription);
        builder.AppendLine(
            "Базовый вариант отличается только стратегией чтения: один раз загружает полный legacy aggregate через " +
            "`GetByExchangeAccountAsync`, включая закрытые lifecycle и `PositionChanges`. Измеряется весь workflow, " +
            "а не отдельный вызов repository.");
        builder.AppendLine(
            "Абсолютный SLA не задаётся; длительности зависят от среды. Lock holding включает commit; commit отдельно " +
            "не измерялся. Диагностические `EXPLAIN ANALYZE` выполняются отдельно в откатываемых транзакциях.");
        builder.AppendLine();
        builder.AppendLine(
            "Все полные `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` планы вместе с SQL и параметрами сохранены в " +
            "[JSON evidence](./position-reconciliation-performance-evidence.json).");
        builder.AppendLine();
        builder.AppendLine("## Воспроизведение");
        builder.AppendLine();
        builder.AppendLine("Из `backend/src` в PowerShell:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine("$env:ITS_DOCKER_ENGINE_VERSION = docker info --format '{{.ServerVersion}}'");
        builder.AppendLine("$env:ITS_RUN_POSITION_RECONCILIATION_PERFORMANCE = '1'");
        builder.AppendLine(
            "dotnet test .\\Intelligence.TradeSystem.Infrastructure.IntegrationTests\\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --no-build --filter \"FullyQualifiedName~PositionReconciliationPerformancePostgreSqlTests\" --logger \"console;verbosity=minimal\"");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Наборы данных");
        builder.AppendLine();
        builder.AppendLine("| Набор | Закрытые lifecycle | PositionChanges | Текущие позиции после seed |");
        builder.AppendLine("|---|---:|---:|---:|");
        foreach (var dataset in evidence.Datasets)
        {
            AppendInvariant(
                $"| {dataset.Name} | {dataset.ClosedLifecycleCount:N0} | {dataset.PositionChangeCount:N0} | Active {dataset.ActivePositions}, Unknown {dataset.UnknownPositions}, Stale {dataset.StalePositions} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Сравнение сценариев");
        builder.AppendLine();
        builder.AppendLine(
            "| Набор | Сценарий | Базовый: SQL round trips / position-state materialized rows / query ms / wall ms | " +
            "Оптимизированный: SQL round trips / position-state materialized rows / query ms / wall ms | " +
            "Заблокировано position rows (базовый → оптимизированный) | " +
            "Lock SQL ms (базовый → оптимизированный) | " +
            "Lock holding ms (базовый → оптимизированный) |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");

        foreach (var dataset in evidence.Datasets)
        {
            foreach (var workflow in Enum.GetValues<PositionReconciliationWorkflow>())
            {
                var baseline = evidence.Measurements
                    .Where(measurement =>
                        measurement.Dataset == dataset.Name &&
                        measurement.Workflow == workflow &&
                        measurement.Implementation == PositionReconciliationImplementation.Baseline &&
                        !measurement.IsWarmup)
                    .ToArray();
                var optimized = evidence.Measurements
                    .Where(measurement =>
                        measurement.Dataset == dataset.Name &&
                        measurement.Workflow == workflow &&
                        measurement.Implementation == PositionReconciliationImplementation.Optimized &&
                        !measurement.IsWarmup)
                    .ToArray();
                AppendInvariant(
                    $"| {dataset.Name} | {WorkflowLabel(workflow)} | {Median(baseline.Select(item => (double)item.SqlRoundTrips)):F0} / {Median(baseline.Select(item => (double)item.MaterializedRows.Total)):F0} / {Median(baseline.Select(item => item.QueryExecutionDurationMilliseconds)):F3} / {Median(baseline.Select(item => item.WallTimeMilliseconds)):F3} | {Median(optimized.Select(item => (double)item.SqlRoundTrips)):F0} / {Median(optimized.Select(item => (double)item.MaterializedRows.Total)):F0} / {Median(optimized.Select(item => item.QueryExecutionDurationMilliseconds)):F3} / {Median(optimized.Select(item => item.WallTimeMilliseconds)):F3} | {Median(baseline.Select(item => (double)item.PositionRowsLocked)):F0} → {Median(optimized.Select(item => (double)item.PositionRowsLocked)):F0} | {Median(baseline.Select(item => item.PositionLockSqlDurationMilliseconds + item.AccountLockSqlDurationMilliseconds)):F3} → {Median(optimized.Select(item => item.PositionLockSqlDurationMilliseconds + item.AccountLockSqlDurationMilliseconds)):F3} | {Median(baseline.Select(item => item.LockHoldingDurationMilliseconds)):F3} → {Median(optimized.Select(item => item.LockHoldingDurationMilliseconds)):F3} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Использованные индексы PostgreSQL");
        builder.AppendLine();
        builder.AppendLine("| Набор | Сценарий | Реализация | Индексы из полных планов |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var group in evidence.Plans
                     .GroupBy(plan => (plan.Dataset, plan.Workflow, plan.Implementation))
                     .OrderBy(group => group.Key.Dataset, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.Workflow)
                     .ThenBy(group => group.Key.Implementation))
        {
            var indexes = group.SelectMany(plan => plan.IndexNames)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            AppendInvariant(
                $"| {group.Key.Dataset} | {WorkflowLabel(group.Key.Workflow)} | {group.Key.Implementation} | {string.Join(", ", indexes.Select(index => $"`{index}`"))} |");
        }

        return builder.ToString();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new InvalidOperationException("В performance evidence отсутствуют повторные измерения.");
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static string WorkflowLabel(PositionReconciliationWorkflow workflow) =>
        workflow switch
        {
            PositionReconciliationWorkflow.WalletObservation => "Наблюдение wallet",
            PositionReconciliationWorkflow.DirectPositionUpdate => "Прямое обновление позиции",
            PositionReconciliationWorkflow.OpenAfterClosure => "Открытие после закрытия",
            PositionReconciliationWorkflow.FullRestReconciliation => "Полная REST reconciliation",
            PositionReconciliationWorkflow.LockAcquisition => "Блокировки position/account",
            _ => throw new ArgumentOutOfRangeException(nameof(workflow), workflow, null),
        };

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Не удалось определить корень repository для benchmark evidence.");
    }

    private sealed class PerformancePositionRepository(
        IPositionRepository inner,
        PositionReconciliationPerformanceAccount account,
        bool baseline,
        PositionMaterializationCounts materialized,
        PerformanceCommandCapture capture)
        : IPositionRepository
    {
        private IReadOnlyCollection<Versioned<Position>>? legacyPositions;
        private readonly Dictionary<PositionId, int> persistedHistoryCounts = [];

        public async Task<IReadOnlyCollection<Versioned<Position>>> GetByExchangeAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            if (baseline)
            {
                return await ReadLegacyAccountAsync(userId, exchangeAccountId, cancellationToken);
            }

            var result = await inner.GetByExchangeAccountAsync(userId, exchangeAccountId, cancellationToken);
            materialized.AddAggregates(result);
            RememberHistoryCounts(result);
            return result;
        }

        public async Task<IReadOnlyCollection<PositionVersionWatermark>> GetCurrentVersionWatermarksAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            if (baseline)
            {
                var positions = await ReadLegacyAccountAsync(userId, exchangeAccountId, cancellationToken);
                return positions
                    .Where(position => position.Value.TrackingState != PositionTrackingState.Closed)
                    .Select(position => new PositionVersionWatermark(position.Value.Id, position.Version))
                    .ToArray();
            }

            var result = await inner.GetCurrentVersionWatermarksAsync(userId, exchangeAccountId, cancellationToken);
            materialized.AddWatermarks(result.Count);
            return result;
        }

        public async Task<IReadOnlyCollection<PositionPortfolioProjection>> GetCurrentPortfolioPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            if (baseline)
            {
                var positions = await ReadLegacyAccountAsync(userId, exchangeAccountId, cancellationToken);
                return positions
                    .Where(position => position.Value.TrackingState != PositionTrackingState.Closed)
                    .Select(PositionPortfolioProjection.From)
                    .ToArray();
            }

            var result = await inner.GetCurrentPortfolioPositionsAsync(userId, exchangeAccountId, cancellationToken);
            materialized.AddProjections(result.Count);
            return result;
        }

        public async Task<IReadOnlyCollection<Versioned<Position>>> GetCurrentWithHistoryAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            if (baseline)
            {
                var positions = await ReadLegacyAccountAsync(userId, exchangeAccountId, cancellationToken);
                return positions
                    .Where(position => position.Value.TrackingState != PositionTrackingState.Closed)
                    .ToArray();
            }

            var result = await inner.GetCurrentWithHistoryAsync(userId, exchangeAccountId, cancellationToken);
            materialized.AddAggregates(result);
            RememberHistoryCounts(result);
            return result;
        }

        public async Task<IReadOnlyCollection<LatestClosedPositionSlot>> GetLatestClosedSlotsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            MarketCategory marketCategory,
            IReadOnlyCollection<PositionSlotKey> slots,
            CancellationToken cancellationToken = default)
        {
            if (baseline)
            {
                var positions = await ReadLegacyAccountAsync(userId, exchangeAccountId, cancellationToken);
                var latest = new List<LatestClosedPositionSlot>();
                foreach (var slot in slots)
                {
                    var closedAt = positions
                        .Select(position => position.Value)
                        .Where(position =>
                            position.TrackingState == PositionTrackingState.Closed &&
                            position.MarketCategory == marketCategory &&
                            position.ExchangePositionKey.PositionIdx == slot.PositionIdx &&
                            string.Equals(
                                position.ExchangePositionKey.InstrumentId.Value,
                                slot.InstrumentId.Value,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(position => position.ClosedAt)
                        .Max();
                    if (closedAt is { } value)
                    {
                        latest.Add(new LatestClosedPositionSlot(slot, value));
                    }
                }

                return latest;
            }

            var uniqueSlots = slots
                .GroupBy(slot => (slot.InstrumentId.Value.ToUpperInvariant(), slot.PositionIdx))
                .Select(group => group.First())
                .ToArray();
            var stopwatch = Stopwatch.StartNew();
            var result = await inner.GetLatestClosedSlotsAsync(
                userId,
                exchangeAccountId,
                marketCategory,
                slots,
                cancellationToken);
            stopwatch.Stop();
            if (uniqueSlots.Length > 0)
            {
                capture.RecordManual(
                    "Reader",
                    PositionRepository.LatestClosedSlotsSql,
                    LatestClosedSlotParameters(userId, exchangeAccountId, marketCategory, uniqueSlots),
                    stopwatch.Elapsed,
                    result.Count);
            }

            materialized.AddClosedSlots(result.Count);
            return result;
        }

        public async Task<Versioned<Position>?> GetByIdAsync(
            UserId userId,
            PositionId id,
            CancellationToken cancellationToken = default)
        {
            if (baseline)
            {
                var positions = await ReadLegacyAccountAsync(
                    userId,
                    ExchangeAccountId.FromGuid(account.AccountId),
                    cancellationToken);
                foreach (var position in positions)
                {
                    if (position.Value.Id == id)
                    {
                        return position;
                    }
                }

                return null;
            }

            var result = await inner.GetByIdAsync(userId, id, cancellationToken);
            if (result is { } targetPosition)
            {
                materialized.AddAggregate(targetPosition.Value);
                RememberHistoryCount(targetPosition.Value);
            }

            return result;
        }

        public async Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            Position position,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default)
        {
            if (expectedVersion is not null &&
                persistedHistoryCounts.TryGetValue(position.Id, out var persistedHistoryCount))
            {
                materialized.AddPositionChanges(persistedHistoryCount);
            }

            var version = await inner.SaveAsync(
                userId,
                position,
                expectedVersion,
                cancellationToken);
            if (expectedVersion is not null)
            {
                persistedHistoryCounts[position.Id] = position.Changes.Count;
            }

            return version;
        }

        private async Task<IReadOnlyCollection<Versioned<Position>>> ReadLegacyAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken)
        {
            if (legacyPositions is not null)
            {
                return legacyPositions;
            }

            legacyPositions = await inner.GetByExchangeAccountAsync(
                userId,
                exchangeAccountId,
                cancellationToken);
            materialized.AddAggregates(legacyPositions);
            RememberHistoryCounts(legacyPositions);
            return legacyPositions;
        }

        private void RememberHistoryCounts(IEnumerable<Versioned<Position>> positions)
        {
            foreach (var position in positions)
            {
                RememberHistoryCount(position.Value);
            }
        }

        private void RememberHistoryCount(Position position) =>
            persistedHistoryCounts[position.Id] = position.Changes.Count;

        private static IReadOnlyCollection<NpgsqlParameter> LatestClosedSlotParameters(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            MarketCategory marketCategory,
            IReadOnlyCollection<PositionSlotKey> slots) =>
        [
            new NpgsqlParameter("instrument_ids", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = slots.Select(slot => slot.InstrumentId.Value).ToArray(),
            },
            new NpgsqlParameter("position_indices", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            {
                Value = slots.Select(slot => slot.PositionIdx).ToArray(),
            },
            new NpgsqlParameter("exchange_account_id", exchangeAccountId.Value),
            new NpgsqlParameter("user_id", userId.Value),
            new NpgsqlParameter("market_category", marketCategory.ToString()),
        ];
    }

    private sealed class PerformanceSyncTransaction(
        TradeSystemDbContext dbContext,
        bool baseline,
        PerformanceCommandCapture capture)
        : IExchangeAccountSyncTransaction
    {
        private const string BaselinePositionLockSql =
            """
            SELECT p.position_id
            FROM positions AS p
            INNER JOIN exchange_accounts AS a
                ON a.exchange_account_id = p.exchange_account_id
            WHERE p.exchange_account_id = @exchange_account_id
              AND a.user_id = @user_id
            ORDER BY p.position_id
            FOR UPDATE OF p
            """;
        private readonly ExchangeAccountSyncTransaction transaction =
            new(dbContext);
        private long? lockStartTimestamp;
        private TimeSpan lockHoldingDuration;
        private TimeSpan positionLockSqlDuration;
        private TimeSpan accountLockSqlDuration;
        private int positionRowsLocked;
        private int accountRowsLocked;

        public TimeSpan PositionLockSqlDuration => positionLockSqlDuration;

        public TimeSpan AccountLockSqlDuration => accountLockSqlDuration;

        public TimeSpan LockHoldingDuration => lockHoldingDuration;

        public int PositionRowsLocked => positionRowsLocked;

        public int AccountRowsLocked => accountRowsLocked;

        public bool LockOrderPreserved { get; private set; } = true;

        public async Task LockPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            lockStartTimestamp ??= Stopwatch.GetTimestamp();
            var parameters = LockParameters(userId, exchangeAccountId);
            var stopwatch = Stopwatch.StartNew();
            if (baseline)
            {
                var rows = await LockAllPositionsAsync(parameters, cancellationToken);
                positionRowsLocked += rows;
                LockOrderPreserved &= BaselinePositionLockSql.Contains(
                    "ORDER BY p.position_id",
                    StringComparison.Ordinal);
                stopwatch.Stop();
                capture.RecordManual(
                    "LockReader",
                    BaselinePositionLockSql,
                    parameters,
                    stopwatch.Elapsed,
                    rows);
            }
            else
            {
                var rows = await ExchangeAccountSerializationLock.LockPositionsAsync(
                    dbContext,
                    userId,
                    exchangeAccountId,
                    cancellationToken);
                positionRowsLocked += rows;
                LockOrderPreserved &= ExchangeAccountSerializationLock.PositionLockSql.Contains(
                    "ORDER BY p.position_id",
                    StringComparison.Ordinal);
                stopwatch.Stop();
                capture.RecordManual(
                    "LockReader",
                    ExchangeAccountSerializationLock.PositionLockSql,
                    parameters,
                    stopwatch.Elapsed,
                    rows);
            }

            positionLockSqlDuration += stopwatch.Elapsed;
        }

        public async Task LockAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            var parameters = LockParameters(userId, exchangeAccountId);
            var stopwatch = Stopwatch.StartNew();
            await ExchangeAccountSerializationLock.LockAsync(
                dbContext,
                userId,
                exchangeAccountId,
                cancellationToken);
            stopwatch.Stop();
            accountLockSqlDuration += stopwatch.Elapsed;
            accountRowsLocked++;
            capture.RecordManual(
                "LockScalar",
                ExchangeAccountSerializationLock.AccountLockSql,
                parameters,
                stopwatch.Elapsed,
                rowsReturned: 1);
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await transaction.ExecuteAsync(operation, cancellationToken);
            }
            finally
            {
                if (lockStartTimestamp is { } startTimestamp)
                {
                    lockHoldingDuration += Stopwatch.GetElapsedTime(startTimestamp);
                    lockStartTimestamp = null;
                }
            }
        }

        private async Task<int> LockAllPositionsAsync(
            IReadOnlyCollection<NpgsqlParameter> parameters,
            CancellationToken cancellationToken)
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = BaselinePositionLockSql;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add((NpgsqlParameter)parameter.Clone());
            }

            var count = 0;
            Guid? previous = null;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var current = reader.GetGuid(0);
                if (previous is { } prior && prior.CompareTo(current) > 0)
                {
                    LockOrderPreserved = false;
                }

                previous = current;
                count++;
            }

            return count;
        }

        private static IReadOnlyCollection<NpgsqlParameter> LockParameters(
            UserId userId,
            ExchangeAccountId exchangeAccountId) =>
        [
            new NpgsqlParameter("exchange_account_id", NpgsqlDbType.Uuid)
            {
                Value = exchangeAccountId.Value,
            },
            new NpgsqlParameter("user_id", NpgsqlDbType.Uuid)
            {
                Value = userId.Value,
            },
        ];
    }

    private sealed class PerformancePrivateAccountProvider(
        DateTimeOffset observedAt,
        IReadOnlyList<OpenPosition> positions)
        : IPrivateAccountProvider
    {
        private readonly AccountBalance balance =
            new(AccountType.Unified, 1_000m, 950m, 800m, 50m, []);

        public Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OpenPositionsObservation> GetOpenPositionsAsync(
            MarketCategory category,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observed = positions
                .Where(position => symbol is null || position.Symbol == symbol)
                .ToArray();
            return Task.FromResult(OpenPositionsObservation.Complete(
                category,
                symbol,
                observedAt,
                observed));
        }

        public Task<AccountBalanceObservation> GetWalletBalanceAsync(
            AccountType accountType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AccountBalanceObservation.Complete(balance, observedAt));
        }
    }

    private sealed class PerformancePrivateAccountProviderFactory(
        IPrivateAccountProvider provider)
        : IPrivateAccountProviderFactory
    {
        public IPrivateAccountProviderLease Create(
            ExchangeId exchange,
            ExchangeAccountCredential credentials) =>
            new PerformancePrivateAccountProviderLease(provider);
    }

    private sealed class PerformancePrivateAccountProviderLease(IPrivateAccountProvider provider)
        : IPrivateAccountProviderLease
    {
        public IPrivateAccountProvider Provider => provider;

        public void Dispose()
        {
        }
    }

    private sealed class PerformanceCredentialStore : IExchangeAccountCredentialStore
    {
        private static readonly ExchangeAccountCredential Credential =
            new(
                new ExchangeAccountCredentialSecret("performance-key", "performance-secret"),
                ConcurrencyVersion.Initial);

        public Task<ExchangeAccountCredential?> GetAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ExchangeAccountCredential?>(Credential);

        public Task<ExchangeAccountCredentialMetadata?> GetMetadataAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> CreateAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ExchangeAccountCredentialSecret secret,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> RotateAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            ExchangeAccountCredentialSecret replacement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> ReprotectAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class WorkflowRun(
        PerformanceMeasurementEvidence measurement,
        IReadOnlyList<CapturedSqlCommand> commands)
    {
        public PerformanceMeasurementEvidence Measurement { get; } = measurement;

        public IReadOnlyList<CapturedSqlCommand> Commands { get; } = commands;
    }

    private sealed record PerformanceEvidence(
        DateTimeOffset GeneratedAt,
        string PostgreSqlImage,
        PerformanceRuntimeEnvironment Runtime,
        PositionReconciliationDatabaseEnvironment Database,
        PerformanceMethodologyEvidence Methodology,
        IReadOnlyList<PerformanceDatasetEvidence> Datasets,
        IReadOnlyList<PerformanceMeasurementEvidence> Measurements,
        IReadOnlyList<PerformanceQueryPlanEvidence> Plans);

    private sealed record PerformanceRuntimeEnvironment(
        string OperatingSystem,
        string ProcessArchitecture,
        string Framework,
        string SdkVersion,
        int ProcessorCount,
        long AvailableMemoryBytes,
        string? DockerEngineVersion);

    private sealed record PerformanceMethodologyEvidence(
        int WarmupCount,
        int MeasurementCount,
        string BaselineDescription,
        string ExplainDescription,
        string LockDurationDescription,
        string QueryDurationDescription,
        string MaterializedRowsDescription);

    private sealed record PerformanceDatasetEvidence(
        string Name,
        int ClosedLifecycleCount,
        int PositionChangeCount,
        int ActivePositions,
        int UnknownPositions,
        int StalePositions);

    private sealed record MaterializedRowsEvidence(
        int Positions,
        int PositionChanges,
        int PortfolioProjections,
        int Watermarks,
        int LatestClosedSlots)
    {
        public int Total =>
            Positions + PositionChanges + PortfolioProjections + Watermarks + LatestClosedSlots;
    }

    private sealed record PerformanceMeasurementEvidence(
        string Dataset,
        PositionReconciliationWorkflow Workflow,
        PositionReconciliationImplementation Implementation,
        int Iteration,
        bool IsWarmup,
        int SqlRoundTrips,
        MaterializedRowsEvidence MaterializedRows,
        double QueryExecutionDurationMilliseconds,
        double SqlCommandDurationMilliseconds,
        double WallTimeMilliseconds,
        int PositionRowsLocked,
        int AccountRowsLocked,
        double PositionLockSqlDurationMilliseconds,
        double AccountLockSqlDurationMilliseconds,
        double LockHoldingDurationMilliseconds,
        double? CommitDurationMilliseconds,
        bool LockOrderPreserved,
        string Outcome,
        IReadOnlyList<PerformanceSqlCommandEvidence> Commands);

    private sealed record PerformanceSqlCommandEvidence(
        int Sequence,
        string CommandType,
        string QueryName,
        double ExecutionDurationMilliseconds,
        int? RowsReturned);

    private sealed record PerformanceSqlParameterEvidence(
        string Name,
        string DataType,
        object? Value);

    private sealed record PerformanceQueryPlanEvidence(
        string Dataset,
        PositionReconciliationWorkflow Workflow,
        PositionReconciliationImplementation Implementation,
        string QueryName,
        string CommandText,
        IReadOnlyList<PerformanceSqlParameterEvidence> Parameters,
        JsonElement ExecutionPlan,
        IReadOnlyList<string> IndexNames,
        bool IsLockingQuery,
        bool RollbackAfterExplain);

    private sealed class PositionMaterializationCounts
    {
        public int Positions { get; private set; }
        public int PositionChanges { get; private set; }
        public int PortfolioProjections { get; private set; }
        public int Watermarks { get; private set; }
        public int LatestClosedSlots { get; private set; }

        public void AddAggregates(IEnumerable<Versioned<Position>> positions)
        {
            var values = positions.ToArray();
            Positions += values.Length;
            PositionChanges += values.Sum(position => position.Value.Changes.Count);
        }

        public void AddAggregate(Position position)
        {
            Positions++;
            PositionChanges += position.Changes.Count;
        }

        public void AddProjections(int count) => PortfolioProjections += count;

        public void AddWatermarks(int count) => Watermarks += count;

        public void AddClosedSlots(int count) => LatestClosedSlots += count;

        public void AddPositionChanges(int count) => PositionChanges += count;

        public MaterializedRowsEvidence ToEvidence() =>
            new(Positions, PositionChanges, PortfolioProjections, Watermarks, LatestClosedSlots);
    }

    private sealed record CapturedSqlCommand(
        int Sequence,
        string CommandType,
        string QueryName,
        TimeSpan ExecutionDuration,
        string CommandText,
        IReadOnlyCollection<NpgsqlParameter> Parameters,
        int? RowsReturned)
    {
        public PerformanceSqlParameterEvidence[] ToParameterEvidence() =>
            Parameters
                .Select(parameter => new PerformanceSqlParameterEvidence(
                    parameter.ParameterName,
                    parameter.NpgsqlDbType.ToString(),
                    ParameterValue(parameter.Value)))
                .ToArray();

        public PerformanceSqlCommandEvidence ToEvidence() =>
            new(
                Sequence,
                CommandType,
                QueryName,
                ExecutionDuration.TotalMilliseconds,
                RowsReturned);

        private static object? ParameterValue(object? value) =>
            value switch
            {
                null or DBNull => null,
                Guid guid => guid.ToString("D"),
                DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
                DateTime timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Array values => values.Cast<object?>().Select(ParameterValue).ToArray(),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
    }

    private sealed class PerformanceCommandCapture : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, PendingCommand> pending = new();
        private readonly List<CapturedSqlCommand> commands = [];
        private readonly object sync = new();
        private int nextSequence;

        public IReadOnlyList<CapturedSqlCommand> Commands
        {
            get
            {
                lock (sync)
                {
                    return commands.OrderBy(command => command.Sequence).ToArray();
                }
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Start(command, eventData, "Reader");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Complete(eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Start(command, eventData, "NonQuery");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            Complete(eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Start(command, eventData, "Scalar");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result,
            CancellationToken cancellationToken = default)
        {
            Complete(eventData);
            return ValueTask.FromResult(result);
        }

        public void RecordManual(
            string commandType,
            string commandText,
            IReadOnlyCollection<NpgsqlParameter> parameters,
            TimeSpan elapsed,
            int? rowsReturned)
        {
            var clonedParameters = parameters
                .Select(parameter => (NpgsqlParameter)parameter.Clone())
                .ToArray();
            Add(commandType, commandText, clonedParameters, elapsed, rowsReturned);
        }

        public void RecordRaw(DbCommand command, TimeSpan elapsed)
        {
            var parameters = command.Parameters.Cast<NpgsqlParameter>()
                .Select(parameter => (NpgsqlParameter)parameter.Clone())
                .ToArray();
            Add("Raw", command.CommandText, parameters, elapsed, rowsReturned: null);
        }

        private void Add(
            string commandType,
            string commandText,
            IReadOnlyCollection<NpgsqlParameter> parameters,
            TimeSpan elapsed,
            int? rowsReturned)
        {
            var command = new CapturedSqlCommand(
                Interlocked.Increment(ref nextSequence),
                commandType,
                QueryName(commandText),
                elapsed,
                commandText,
                parameters,
                rowsReturned);
            lock (sync)
            {
                commands.Add(command);
            }
        }

        private void Start(DbCommand command, CommandEventData eventData, string commandType)
        {
            var parameters = command.Parameters.Cast<NpgsqlParameter>()
                .Select(parameter => (NpgsqlParameter)parameter.Clone())
                .ToArray();
            pending[eventData.CommandId] = new PendingCommand(
                commandType,
                QueryName(command.CommandText),
                command.CommandText,
                parameters,
                Stopwatch.StartNew());
        }

        private void Complete(CommandEventData eventData)
        {
            if (!pending.TryRemove(eventData.CommandId, out var pendingCommand))
            {
                throw new InvalidOperationException(
                    $"SQL-команда {eventData.CommandId} завершилась без зафиксированного начала.");
            }

            pendingCommand.Stopwatch.Stop();
            var command = new CapturedSqlCommand(
                Interlocked.Increment(ref nextSequence),
                pendingCommand.CommandType,
                pendingCommand.QueryName,
                pendingCommand.Stopwatch.Elapsed,
                pendingCommand.CommandText,
                pendingCommand.Parameters,
                RowsReturned: null);
            lock (sync)
            {
                commands.Add(command);
            }
        }

        private static string QueryName(string sql)
        {
            if (sql.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase))
                return "account-lock";
            if (sql.Contains("FOR UPDATE OF p", StringComparison.OrdinalIgnoreCase))
                return "position-lock";
            if (sql.Contains("unnest", StringComparison.OrdinalIgnoreCase) &&
                sql.Contains("closed_at", StringComparison.OrdinalIgnoreCase))
                return "latest-closed-slots";
            if (sql.Contains("position_changes", StringComparison.OrdinalIgnoreCase))
                return "position-changes";
            if (sql.Contains("positions", StringComparison.OrdinalIgnoreCase))
                return "positions";
            if (sql.Contains("portfolio_states", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("portfolio_position_states", StringComparison.OrdinalIgnoreCase))
                return "portfolio";
            if (sql.Contains("position_observation_cursors", StringComparison.OrdinalIgnoreCase))
                return "position-cursor";
            if (sql.Contains("exchange_wallet_observation_cursors", StringComparison.OrdinalIgnoreCase))
                return "wallet-cursor";
            if (sql.Contains("exchange_realtime_observations", StringComparison.OrdinalIgnoreCase))
                return "realtime-state";
            if (sql.Contains("exchange_rest_ordering_intervals", StringComparison.OrdinalIgnoreCase))
                return "rest-ordering-evidence";
            if (sql.Contains("outbox_messages", StringComparison.OrdinalIgnoreCase))
                return "outbox";
            if (sql.Contains("exchange_accounts", StringComparison.OrdinalIgnoreCase))
                return "account";
            return "other";
        }

        private sealed record PendingCommand(
            string CommandType,
            string QueryName,
            string CommandText,
            IReadOnlyCollection<NpgsqlParameter> Parameters,
            Stopwatch Stopwatch);
    }
}

internal sealed class PositionReconciliationPerformanceFactAttribute : FactAttribute
{
    public PositionReconciliationPerformanceFactAttribute()
    {
        DisplayName = "PostgreSQL benchmark согласования позиций";
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ITS_RUN_POSITION_RECONCILIATION_PERFORMANCE"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Установите ITS_RUN_POSITION_RECONCILIATION_PERFORMANCE=1 для запуска PostgreSQL benchmark.";
        }
    }
}
