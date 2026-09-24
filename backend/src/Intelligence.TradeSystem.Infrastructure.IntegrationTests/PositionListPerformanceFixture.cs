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

        var users = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var accounts = new List<(Guid Id, Guid UserId)>();
        for (var userIndex = 0; userIndex < users.Length; userIndex++)
        {
            for (var accountIndex = 0; accountIndex < accountsPerUser; accountIndex++)
            {
                accounts.Add((Guid.NewGuid(), users[userIndex]));
            }
        }

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
                var account = accounts[index % accounts.Count];
                var state = index % 10 == 0
                    ? "Active"
                    : index % 10 == 1
                        ? "Unknown"
                        : index % 10 == 2
                            ? "Stale"
                            : "Closed";
                var detectedAt = anchor.AddMinutes(-(index % 100_000));
                var positionId = DeterministicGuid(index);

                importer.StartRow();
                importer.Write(positionId, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write(account.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
                importer.Write($"SYMBOL{index % 40:D2}", NpgsqlTypes.NpgsqlDbType.Varchar);
                importer.Write(index % 2 == 0 ? "Long" : "Short", NpgsqlTypes.NpgsqlDbType.Varchar);
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

    private static Guid DeterministicGuid(int index)
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(), index);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), ~index);
        return new Guid(bytes);
    }
}
