using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

internal sealed class PositionHistoryReadPerformanceFixture : IAsyncDisposable
{
    private static readonly int[] ClosedLifecycleCounts = [10, 1_000, 10_000];
    private static readonly DateTimeOffset Anchor =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public IReadOnlyList<Scenario> Scenarios { get; private set; } = [];

    public async Task StartAsync()
    {
        await postgres.StartAsync();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await SeedAsync();
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

    public async ValueTask DisposeAsync() => await postgres.DisposeAsync();

    private async Task SeedAsync()
    {
        var userId = Guid.Parse("00000000-0000-0000-0001-000000000001");
        var scenarios = ClosedLifecycleCounts
            .Select((closedCount, index) => new Scenario(
                $"{closedCount:N0}/{closedCount * 10:N0}",
                Guid.Parse($"00000000-0000-0000-0002-{index + 1:D12}"),
                userId,
                closedCount))
            .ToArray();
        Scenarios = scenarios;

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
            foreach (var scenario in scenarios)
            {
                importer.StartRow();
                importer.Write(scenario.AccountId, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write(scenario.UserId, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write("Bybit", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write($"provider-{scenario.AccountId:N}", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write("Performance", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write("Connected", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(3, NpgsqlTypes.NpgsqlDbType.Integer);
                importer.Write(1L, NpgsqlTypes.NpgsqlDbType.Bigint);
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
            var positionIndex = 0;
            foreach (var scenario in scenarios)
            {
                for (var closedIndex = 0; closedIndex < scenario.ClosedLifecycleCount; closedIndex++)
                {
                    var positionId = DeterministicPositionGuid(positionIndex);
                    var detectedAt = Anchor.AddMinutes(positionIndex);
                    WritePosition(
                        importer,
                        scenario,
                        positionIndex++,
                        closed: true,
                        positionIdx: 0,
                        detectedAt);
                }

                for (var activeIndex = 0; activeIndex < 5; activeIndex++)
                {
                    var detectedAt = Anchor.AddMinutes(positionIndex);
                    WritePosition(
                        importer,
                        scenario,
                        positionIndex++,
                        closed: false,
                        positionIdx: 100_000 + activeIndex,
                        detectedAt);
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
            var positionIndex = 0;
            foreach (var scenario in scenarios)
            {
                for (var closedIndex = 0; closedIndex < scenario.ClosedLifecycleCount; closedIndex++)
                {
                    WriteHistory(
                        importer,
                        DeterministicPositionGuid(positionIndex),
                        Anchor.AddMinutes(positionIndex),
                        10,
                        closed: true);
                    positionIndex++;
                }

                for (var activeIndex = 0; activeIndex < 5; activeIndex++)
                {
                    WriteHistory(
                        importer,
                        DeterministicPositionGuid(positionIndex),
                        Anchor.AddMinutes(positionIndex),
                        1,
                        closed: false);
                    positionIndex++;
                }
            }

            await importer.CompleteAsync();
        }

        await using var analyze = connection.CreateCommand();
        analyze.CommandText = "ANALYZE exchange_accounts; ANALYZE positions; ANALYZE position_changes;";
        await analyze.ExecuteNonQueryAsync();
    }

    private static void WritePosition(
        NpgsqlBinaryImporter importer,
        Scenario scenario,
        int index,
        bool closed,
        int positionIdx,
        DateTimeOffset detectedAt)
    {
        importer.StartRow();
        importer.Write(DeterministicPositionGuid(index), NpgsqlTypes.NpgsqlDbType.Uuid);
        importer.Write(scenario.AccountId, NpgsqlTypes.NpgsqlDbType.Uuid);
        importer.Write("BTCUSDT", NpgsqlTypes.NpgsqlDbType.Varchar);
        importer.Write(index % 2 == 0 ? "Long" : "Short", NpgsqlTypes.NpgsqlDbType.Varchar);
        importer.Write(positionIdx, NpgsqlTypes.NpgsqlDbType.Integer);
        importer.Write("Linear", NpgsqlTypes.NpgsqlDbType.Varchar);
        importer.Write("USDT", NpgsqlTypes.NpgsqlDbType.Varchar);
        importer.Write(1m, NpgsqlTypes.NpgsqlDbType.Numeric);
        importer.Write(detectedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz);
        importer.Write(
            closed ? detectedAt.AddMinutes(9) : detectedAt.AddMinutes(1),
            NpgsqlTypes.NpgsqlDbType.TimestampTz);
        if (closed)
        {
            importer.Write(detectedAt.AddMinutes(10), NpgsqlTypes.NpgsqlDbType.TimestampTz);
        }
        else
        {
            importer.WriteNull();
        }

        importer.Write(closed ? "Closed" : "Active", NpgsqlTypes.NpgsqlDbType.Varchar);
        importer.Write(1L, NpgsqlTypes.NpgsqlDbType.Bigint);
    }

    private static void WriteHistory(
        NpgsqlBinaryImporter importer,
        Guid positionId,
        DateTimeOffset detectedAt,
        int changes,
        bool closed)
    {
        for (var sequence = 1; sequence <= changes; sequence++)
        {
            var isFirst = sequence == 1;
            var isClosed = closed && sequence == changes;
            importer.StartRow();
            importer.Write(positionId, NpgsqlTypes.NpgsqlDbType.Uuid);
            importer.Write(sequence, NpgsqlTypes.NpgsqlDbType.Integer);
            importer.Write(
                isFirst ? "New" : isClosed ? "Closed" : "Updated",
                NpgsqlTypes.NpgsqlDbType.Varchar);
            importer.Write(
                isFirst
                    ? "InitialObservation"
                    : isClosed
                        ? "MissingFromCompleteObservation"
                        : "ExchangeObservation",
                NpgsqlTypes.NpgsqlDbType.Varchar);
            importer.Write(
                detectedAt.AddMinutes(sequence),
                NpgsqlTypes.NpgsqlDbType.TimestampTz);
            importer.Write(
                isClosed ? "Closed" : "Active",
                NpgsqlTypes.NpgsqlDbType.Varchar);
            importer.Write(1m, NpgsqlTypes.NpgsqlDbType.Numeric);
        }
    }

    private static Guid DeterministicPositionGuid(int index) =>
        Guid.Parse($"00000000-0000-0000-0003-{index + 1:D12}");

    internal sealed record Scenario(
        string Name,
        Guid AccountId,
        Guid UserId,
        int ClosedLifecycleCount);
}
