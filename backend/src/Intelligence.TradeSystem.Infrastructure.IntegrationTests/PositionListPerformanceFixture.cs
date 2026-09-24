using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

internal sealed class PositionListPerformanceFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public Guid TargetUserId { get; private set; }

    public IReadOnlyList<Guid> TargetAccountIds { get; private set; } = [];

    public async Task StartAsync(int accountsPerUser = 3)
    {
        await postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await SeedAsync(accountsPerUser);
    }

    public TradeSystemDbContext CreateContext() =>
        CreateContext(null);

    public TradeSystemDbContext CreateContext(DbCommandInterceptor? interceptor) =>
        CreateOptions(interceptor) is { } options
            ? new TradeSystemDbContext(options)
            : throw new InvalidOperationException("Unable to create benchmark context.");

    private DbContextOptions<TradeSystemDbContext> CreateOptions(DbCommandInterceptor? interceptor)
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

        return builder.Options;
    }

    public async ValueTask DisposeAsync() => await postgres.DisposeAsync();

    private async Task SeedAsync(int accountsPerUser)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var users = Enumerable.Range(0, 8).Select(DeterministicUserGuid).ToArray();
        var accounts = new List<(Guid Id, Guid UserId)>();
        for (var userIndex = 0; userIndex < users.Length; userIndex++)
        {
            for (var accountIndex = 0; accountIndex < accountsPerUser; accountIndex++)
            {
                accounts.Add((
                    DeterministicAccountGuid(userIndex, accountIndex),
                    users[userIndex]));
            }
        }

        TargetUserId = users[0];
        TargetAccountIds = accounts
            .Where(account => account.UserId == TargetUserId)
            .Select(account => account.Id)
            .ToArray();

        await using (var importer = connection.BeginBinaryImport(
            """
            COPY exchange_accounts
            (exchange_account_id, user_id, exchange_id, provider_account_id,
             connection_status, capabilities, last_synced_at, last_error,
             last_applied_balance_observation_at, last_applied_positions_observation_at, version)
            FROM STDIN (FORMAT BINARY)
            """))
        {
            foreach (var account in accounts)
            {
                importer.StartRow();
                importer.Write(account.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write(account.UserId, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write("Bybit", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write($"provider-{account.Id:N}", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write("Connected", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(3, NpgsqlTypes.NpgsqlDbType.Integer);
                importer.WriteNull();
                importer.WriteNull();
                importer.WriteNull();
                importer.WriteNull();
                importer.Write(1L, NpgsqlTypes.NpgsqlDbType.Bigint);
            }

            await importer.CompleteAsync();
        }

        var anchor = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var importer = connection.BeginBinaryImport(
            """
            COPY positions
            (position_id, exchange_account_id, instrument_id, position_side, position_idx,
             market_category, size, average_entry_price, position_value, leverage, mark_price,
             break_even_price, liquidation_price, unrealized_pnl, take_profit, stop_loss,
             trailing_stop, first_detected_at, last_observed_at, closed_at, tracking_state, version)
            FROM STDIN (FORMAT BINARY)
            """))
        {
            for (var index = 0; index < 250_000; index++)
            {
                var account = accounts[Mix(index, 17) % accounts.Count];
                var stateBucket = Mix(index, 23) % 100;
                var state = stateBucket < 4
                    ? "Active"
                    : stateBucket < 7
                        ? "Unknown"
                        : stateBucket < 10
                            ? "Stale"
                            : "Closed";
                var detectedAt = anchor.AddMinutes(-(Mix(index, 31) % 100_000));
                var positionId = DeterministicGuid(index);

                importer.StartRow();
                importer.Write(positionId, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write(account.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write($"SYMBOL{Mix(index, 47) % 40:D2}", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(Mix(index, 61) % 2 == 0 ? "Long" : "Short", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(index, NpgsqlTypes.NpgsqlDbType.Integer);
                importer.Write("Linear", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(1m, NpgsqlTypes.NpgsqlDbType.Numeric);
                importer.Write(100m, NpgsqlTypes.NpgsqlDbType.Numeric);
                importer.Write(100m, NpgsqlTypes.NpgsqlDbType.Numeric);
                importer.Write(2m, NpgsqlTypes.NpgsqlDbType.Numeric);
                importer.Write(100m, NpgsqlTypes.NpgsqlDbType.Numeric);
                importer.WriteNull();
                importer.WriteNull();
                importer.Write(0m, NpgsqlTypes.NpgsqlDbType.Numeric);
                importer.WriteNull();
                importer.WriteNull();
                importer.WriteNull();
                importer.Write(detectedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz);
                importer.Write(detectedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz);
                if (state == "Closed")
                {
                    importer.Write(detectedAt.AddMinutes(1), NpgsqlTypes.NpgsqlDbType.TimestampTz);
                }
                else
                {
                    importer.WriteNull();
                }

                importer.Write(state, NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(1L, NpgsqlTypes.NpgsqlDbType.Bigint);
            }

            await importer.CompleteAsync();
        }

        await using var analyze = connection.CreateCommand();
        analyze.CommandText = "ANALYZE exchange_accounts; ANALYZE positions;";
        await analyze.ExecuteNonQueryAsync();
    }

    private static Guid DeterministicUserGuid(int index) =>
        Guid.Parse($"00000000-0000-0000-0001-{index:D12}");

    private static Guid DeterministicAccountGuid(int userIndex, int accountIndex) =>
        Guid.Parse($"00000000-0000-0000-0002-{userIndex * 1000 + accountIndex:D12}");

    private static Guid DeterministicGuid(int index) =>
        Guid.Parse($"00000000-0000-0000-0003-{index:D12}");

    private static int Mix(int value, int salt)
    {
        unchecked
        {
            var mixed = value * 1_664_525 + salt * 1_013_904_223;
            mixed ^= mixed >> 16;
            mixed *= unchecked((int)2_246_822_519u);
            return mixed & int.MaxValue;
        }
    }
}
