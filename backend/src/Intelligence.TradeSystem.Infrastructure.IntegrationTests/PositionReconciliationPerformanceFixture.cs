using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

internal enum PositionReconciliationWorkflow
{
    WalletObservation,
    DirectPositionUpdate,
    OpenAfterClosure,
    FullRestReconciliation,
    LockAcquisition,
}

internal enum PositionReconciliationImplementation
{
    Baseline,
    Optimized,
}

internal sealed record PositionReconciliationPerformanceDataset(
    string Name,
    int ClosedLifecycleCount,
    int PositionChangeCount,
    IReadOnlyList<PositionReconciliationPerformanceAccount> Accounts)
{
    public PositionReconciliationPerformanceAccount GetAccount(
        PositionReconciliationWorkflow workflow,
        PositionReconciliationImplementation implementation) =>
        Accounts.Single(account =>
            account.Workflow == workflow &&
            account.Implementation == implementation);
}

internal sealed record PositionReconciliationPerformanceAccount(
    Guid AccountId,
    Guid UserId,
    PositionReconciliationWorkflow Workflow,
    PositionReconciliationImplementation Implementation,
    int PositionStartIndex,
    Guid TargetActivePositionId);

internal sealed record PositionReconciliationDatabaseEnvironment(
    string ServerVersion,
    string SharedBuffers,
    string WorkMem,
    string EffectiveCacheSize,
    string MaxParallelWorkers,
    string RandomPageCost);

internal sealed class PositionReconciliationPerformanceFixture : IAsyncDisposable
{
    private const int CurrentPositionCount = 7;
    private const int CurrentPositionChangeCount = 11;
    private const int ClosedPositionIndexBase = 1_000;
    private static readonly int[] ClosedLifecycleCounts = [10, 1_000, 10_000];
    private static readonly PositionReconciliationWorkflow[] Workflows =
    [
        PositionReconciliationWorkflow.WalletObservation,
        PositionReconciliationWorkflow.DirectPositionUpdate,
        PositionReconciliationWorkflow.OpenAfterClosure,
        PositionReconciliationWorkflow.FullRestReconciliation,
        PositionReconciliationWorkflow.LockAcquisition,
    ];
    private static readonly DateTimeOffset Anchor =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserGuid =
        Guid.Parse("00000000-0000-0000-0001-000000000001");
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public IReadOnlyList<PositionReconciliationPerformanceDataset> Datasets { get; private set; } = [];

    public async Task StartAsync()
    {
        await postgres.StartAsync();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        Datasets = CreateDatasets();
        await SeedAsync();
        await SeedRealtimeStateAsync();
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

    public async Task<PositionReconciliationDatabaseEnvironment> ReadDatabaseEnvironmentAsync()
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

        return new PositionReconciliationDatabaseEnvironment(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    public async ValueTask DisposeAsync() => await postgres.DisposeAsync();

    internal static Guid PositionGuid(int index) =>
        Guid.Parse($"00000000-0000-0000-0003-{index + 1:D12}");

    private List<PositionReconciliationPerformanceDataset> CreateDatasets()
    {
        var datasets = new List<PositionReconciliationPerformanceDataset>(ClosedLifecycleCounts.Length);
        var nextAccountIndex = 0;
        var nextPositionIndex = 0;
        foreach (var closedCount in ClosedLifecycleCounts)
        {
            var accounts = new List<PositionReconciliationPerformanceAccount>();
            foreach (var workflow in Workflows)
            {
                foreach (var implementation in Enum.GetValues<PositionReconciliationImplementation>())
                {
                    var positionStartIndex = nextPositionIndex;
                    accounts.Add(new PositionReconciliationPerformanceAccount(
                        AccountGuid(++nextAccountIndex),
                        UserGuid,
                        workflow,
                        implementation,
                        positionStartIndex,
                        PositionGuid(positionStartIndex + closedCount)));
                    nextPositionIndex += closedCount + CurrentPositionCount;
                }
            }

            datasets.Add(new PositionReconciliationPerformanceDataset(
                $"{closedCount:N0}/{closedCount * 10:N0}",
                closedCount,
                closedCount * 10,
                accounts));
        }

        return datasets;
    }

    private async Task SeedAsync()
    {
        var accounts = Datasets.SelectMany(dataset => dataset.Accounts).ToArray();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var importer = connection.BeginBinaryImport(
            """
            COPY exchange_accounts
            (exchange_account_id, user_id, exchange_id, provider_account_id, display_name,
             connection_status, capabilities, version)
            FROM STDIN (FORMAT BINARY)
            """))
        {
            var accountIndex = 0;
            foreach (var account in accounts)
            {
                importer.StartRow();
                importer.Write(account.AccountId, NpgsqlDbType.Uuid);
                importer.Write(account.UserId, NpgsqlDbType.Uuid);
                importer.Write("Bybit", NpgsqlDbType.Varchar);
                importer.Write($"performance-{++accountIndex}", NpgsqlDbType.Varchar);
                importer.Write("Performance", NpgsqlDbType.Varchar);
                importer.Write("Connected", NpgsqlDbType.Varchar);
                importer.Write(3, NpgsqlDbType.Integer);
                importer.Write(1L, NpgsqlDbType.Bigint);
            }

            await importer.CompleteAsync();
        }

        await using (var importer = connection.BeginBinaryImport(
            """
            COPY positions
            (position_id, exchange_account_id, instrument_id, position_side, position_idx,
             market_category, settlement_asset, size, first_detected_at, last_observed_at,
             closed_at, tracking_state, version)
            FROM STDIN (FORMAT BINARY)
            """))
        {
            foreach (var dataset in Datasets)
            {
                foreach (var account in dataset.Accounts)
                {
                    for (var closedIndex = 0; closedIndex < dataset.ClosedLifecycleCount; closedIndex++)
                    {
                        var historyCount = ClosedHistoryCount(dataset.ClosedLifecycleCount, closedIndex);
                        WritePosition(
                            importer,
                            account,
                            PositionGuid(account.PositionStartIndex + closedIndex),
                            closedIndex,
                            ClosedPositionIndexBase + closedIndex,
                            PositionTrackingState.Closed,
                            historyCount);
                    }

                    for (var currentIndex = 0; currentIndex < CurrentPositionCount; currentIndex++)
                    {
                        var trackingState = currentIndex switch
                        {
                            < 5 => PositionTrackingState.Active,
                            5 => PositionTrackingState.Unknown,
                            _ => PositionTrackingState.Stale,
                        };
                        WritePosition(
                            importer,
                            account,
                            PositionGuid(account.PositionStartIndex + dataset.ClosedLifecycleCount + currentIndex),
                            dataset.ClosedLifecycleCount + currentIndex,
                            currentIndex,
                            trackingState,
                            CurrentHistoryCount(currentIndex));
                    }
                }
            }

            await importer.CompleteAsync();
        }

        await using (var importer = connection.BeginBinaryImport(
            """
            COPY position_changes
            (position_id, sequence, kind, cause, occurred_at, tracking_state_after, after_size)
            FROM STDIN (FORMAT BINARY)
            """))
        {
            foreach (var dataset in Datasets)
            {
                foreach (var account in dataset.Accounts)
                {
                    for (var closedIndex = 0; closedIndex < dataset.ClosedLifecycleCount; closedIndex++)
                    {
                        WriteHistory(
                            importer,
                            PositionGuid(account.PositionStartIndex + closedIndex),
                            closedIndex,
                            ClosedHistoryCount(dataset.ClosedLifecycleCount, closedIndex),
                            PositionTrackingState.Closed);
                    }

                    for (var currentIndex = 0; currentIndex < CurrentPositionCount; currentIndex++)
                    {
                        var trackingState = currentIndex switch
                        {
                            < 5 => PositionTrackingState.Active,
                            5 => PositionTrackingState.Unknown,
                            _ => PositionTrackingState.Stale,
                        };
                        WriteHistory(
                            importer,
                            PositionGuid(account.PositionStartIndex + dataset.ClosedLifecycleCount + currentIndex),
                            dataset.ClosedLifecycleCount + currentIndex,
                            CurrentHistoryCount(currentIndex),
                            trackingState);
                    }
                }
            }

            await importer.CompleteAsync();
        }

        await using var analyze = connection.CreateCommand();
        analyze.CommandText =
            "ANALYZE exchange_accounts; ANALYZE positions; ANALYZE position_changes;";
        await analyze.ExecuteNonQueryAsync();
    }

    private async Task SeedRealtimeStateAsync()
    {
        foreach (var account in Datasets
                     .SelectMany(dataset => dataset.Accounts)
                     .Where(account => account.Workflow is
                         PositionReconciliationWorkflow.WalletObservation or
                         PositionReconciliationWorkflow.DirectPositionUpdate or
                         PositionReconciliationWorkflow.OpenAfterClosure))
        {
            await using var context = CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var stateStore = new RealtimeObservationStateStore(context);
            var initial = await stateStore.AdvanceGenerationAsync(
                UserId.FromGuid(account.UserId),
                ExchangeAccountId.FromGuid(account.AccountId),
                RealtimeObservationTransportStatus.Connected);
            var ready = initial.Value with
            {
                LastConnectedAt = Anchor,
                LastHeartbeatAt = Anchor,
                LastFullyReconciledAt = Anchor,
                RecoveryRequired = false,
            };
            await stateStore.SaveAsync(
                UserId.FromGuid(account.UserId),
                ready,
                initial.Version);

            if (account.Workflow == PositionReconciliationWorkflow.DirectPositionUpdate)
            {
                var cursorKey = PositionObservationCursorKey.Create(
                    ExchangeAccountId.FromGuid(account.AccountId),
                    MarketCategory.Linear,
                    InstrumentId.From("BTCUSDT"),
                    0);
                await new PositionObservationCursorStore(context).SaveAsync(
                    UserId.FromGuid(account.UserId),
                    new PositionObservationCursor
                    {
                        Key = cursorKey,
                        LastProviderSequence = 1,
                        LastProviderUpdatedAt = Anchor,
                        LastMessageCreatedAt = Anchor,
                        LastAcceptedFingerprint = "seed",
                        LastDirectAppliedPositionId = PositionId.FromGuid(account.TargetActivePositionId),
                        LastDirectAppliedPositionVersion = ConcurrencyVersion.Initial,
                    },
                    expectedVersion: null,
                    expectedGeneration: ready.Generation);
            }

            await transaction.CommitAsync();
        }
    }

    private static void WritePosition(
        NpgsqlBinaryImporter importer,
        PositionReconciliationPerformanceAccount account,
        Guid positionId,
        int indexWithinAccount,
        int positionIdx,
        PositionTrackingState trackingState,
        int historyCount)
    {
        var detectedAt = Anchor.AddMinutes(indexWithinAccount);
        var lastObservedAt = trackingState switch
        {
            PositionTrackingState.Closed => detectedAt.AddMinutes(historyCount - 1),
            PositionTrackingState.Unknown or PositionTrackingState.Stale => detectedAt.AddMinutes(1),
            _ => detectedAt.AddMinutes(historyCount),
        };
        importer.StartRow();
        importer.Write(positionId, NpgsqlDbType.Uuid);
        importer.Write(account.AccountId, NpgsqlDbType.Uuid);
        importer.Write("BTCUSDT", NpgsqlDbType.Varchar);
        importer.Write("Long", NpgsqlDbType.Varchar);
        importer.Write(positionIdx, NpgsqlDbType.Integer);
        importer.Write("Linear", NpgsqlDbType.Varchar);
        importer.Write("USDT", NpgsqlDbType.Varchar);
        importer.Write(1m, NpgsqlDbType.Numeric);
        importer.Write(detectedAt, NpgsqlDbType.TimestampTz);
        importer.Write(lastObservedAt, NpgsqlDbType.TimestampTz);
        if (trackingState == PositionTrackingState.Closed)
        {
            importer.Write(detectedAt.AddMinutes(historyCount), NpgsqlDbType.TimestampTz);
        }
        else
        {
            importer.WriteNull();
        }

        importer.Write(trackingState.ToString(), NpgsqlDbType.Varchar);
        importer.Write(1L, NpgsqlDbType.Bigint);
    }

    private static void WriteHistory(
        NpgsqlBinaryImporter importer,
        Guid positionId,
        int indexWithinAccount,
        int changes,
        PositionTrackingState finalState)
    {
        for (var sequence = 1; sequence <= changes; sequence++)
        {
            var isFirst = sequence == 1;
            var isLast = sequence == changes;
            var kind = isFirst
                ? PositionChangeKind.New
                : isLast
                    ? finalState switch
                    {
                        PositionTrackingState.Closed => PositionChangeKind.Closed,
                        PositionTrackingState.Unknown => PositionChangeKind.MarkedUnknown,
                        PositionTrackingState.Stale => PositionChangeKind.MarkedStale,
                        _ => PositionChangeKind.Updated,
                    }
                    : PositionChangeKind.Updated;
            var cause = isFirst
                ? PositionChangeCause.InitialObservation
                : isLast
                    ? finalState switch
                    {
                        PositionTrackingState.Closed => PositionChangeCause.MissingFromCompleteObservation,
                        PositionTrackingState.Unknown => PositionChangeCause.PositionsObservationFailed,
                        PositionTrackingState.Stale => PositionChangeCause.FreshnessExpired,
                        _ => PositionChangeCause.ExchangeObservation,
                    }
                    : PositionChangeCause.ExchangeObservation;
            var stateAfter = isLast ? finalState : PositionTrackingState.Active;
            importer.StartRow();
            importer.Write(positionId, NpgsqlDbType.Uuid);
            importer.Write(sequence, NpgsqlDbType.Integer);
            importer.Write(kind.ToString(), NpgsqlDbType.Varchar);
            importer.Write(cause.ToString(), NpgsqlDbType.Varchar);
            importer.Write(
                Anchor.AddMinutes(indexWithinAccount + sequence),
                NpgsqlDbType.TimestampTz);
            importer.Write(stateAfter.ToString(), NpgsqlDbType.Varchar);
            importer.Write(1m, NpgsqlDbType.Numeric);
        }
    }

    private static int ClosedHistoryCount(int closedLifecycleCount, int closedIndex)
    {
        var totalClosedChanges = closedLifecycleCount * 10 - CurrentPositionChangeCount;
        var baseChanges = totalClosedChanges / closedLifecycleCount;
        var remainder = totalClosedChanges % closedLifecycleCount;
        return baseChanges + (closedIndex < remainder ? 1 : 0);
    }

    private static int CurrentHistoryCount(int currentIndex) =>
        currentIndex switch
        {
            0 => 3,
            1 or 2 or 3 or 4 => 1,
            _ => 2,
        };

    private static Guid AccountGuid(int index) =>
        Guid.Parse($"00000000-0000-0000-0002-{index:D12}");
}
