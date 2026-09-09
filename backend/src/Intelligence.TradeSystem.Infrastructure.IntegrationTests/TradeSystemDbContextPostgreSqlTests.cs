using System.Globalization;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class TradeSystemDbContextPostgreSqlTests : IAsyncLifetime
{
    private const string C06Migration = "20260907131212_AddUserIsolationIndexes";
    private const string D04Migration = "20260909092129_AddExchangeAccountSyncWatermark";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem_migrations")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Migrations_apply_to_an_empty_database_without_pending_migrations()
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options;

        await using var dbContext = new TradeSystemDbContext(options);

        Assert.Empty(await dbContext.Database.GetAppliedMigrationsAsync());
        await dbContext.Database.MigrateAsync();
        Assert.NotEmpty(await dbContext.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());

        await dbContext.Database.MigrateAsync("0");
        Assert.Empty(await dbContext.Database.GetAppliedMigrationsAsync());

        await dbContext.Database.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task AddConcurrencyVersion_migration_backfills_pre_existing_rows_to_version_one()
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options;

        await using (var dbContext = new TradeSystemDbContext(options))
        {
            await dbContext.Database.MigrateAsync("20260904132342_InitialDomainPersistence");

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                INSERT INTO exchange_accounts (
                    exchange_account_id, user_id, exchange_id, connection_status, capabilities,
                    last_synced_at, last_error)
                VALUES (
                    '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222',
                    'Bybit', 'Connected', 3, NULL, NULL)
                """;
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using (var dbContext = new TradeSystemDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = """
                SELECT version FROM exchange_accounts
                WHERE exchange_account_id = '11111111-1111-1111-1111-111111111111'
                """;
            var version = (long)(await selectCommand.ExecuteScalarAsync())!;

            Assert.Equal(1L, version);
        }
    }

    [Fact]
    public async Task Credential_migration_preserves_existing_account_data()
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options;

        await using (var dbContext = new TradeSystemDbContext(options))
        {
            await dbContext.Database.MigrateAsync(C06Migration);

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                INSERT INTO exchange_accounts (
                    exchange_account_id, user_id, exchange_id, connection_status, capabilities,
                    last_synced_at, last_error, version)
                VALUES (
                    '33333333-3333-3333-3333-333333333333',
                    '44444444-4444-4444-4444-444444444444',
                    'Bybit', 'Connected', 3, NULL, NULL, 1)
                """;
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using (var dbContext = new TradeSystemDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            var account = await dbContext.ExchangeAccounts
                .SingleAsync(row => row.Id == Guid.Parse("33333333-3333-3333-3333-333333333333"));
            Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), account.UserId);
            Assert.Null(account.LastAppliedBalanceObservationAt);
            Assert.Null(account.LastAppliedPositionsObservationAt);
            Assert.True(await dbContext.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'public'
                          AND table_name = 'exchange_account_credentials')
                    AS "Value"
                    """)
                .SingleAsync());
            var ciphertextConstraint = await dbContext.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT pg_get_constraintdef(oid) AS "Value"
                    FROM pg_constraint
                    WHERE conname = 'ck_exchange_account_credentials_ciphertext_max_length'
                    """)
                .SingleAsync();
            Assert.Contains(
                CredentialProtectionLimits.MaximumPayloadBytes.ToString(CultureInfo.InvariantCulture),
                ciphertextConstraint);
        }
    }

    [Fact]
    public async Task Independent_watermarks_preserve_the_legacy_observation_lower_bound()
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options;

        await using (var dbContext = new TradeSystemDbContext(options))
        {
            await dbContext.Database.MigrateAsync(D04Migration);

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                INSERT INTO exchange_accounts (
                    exchange_account_id, user_id, exchange_id, connection_status, capabilities,
                    last_synced_at, last_error, last_applied_observation_at, version)
                VALUES (
                    '55555555-5555-5555-5555-555555555555',
                    '66666666-6666-6666-6666-666666666666',
                    'Bybit', 'Connected', 3, NULL, NULL,
                    '2026-09-09T10:00:00+00:00', 1)
                """;
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using (var dbContext = new TradeSystemDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            var account = await dbContext.ExchangeAccounts
                .SingleAsync(row => row.Id == Guid.Parse("55555555-5555-5555-5555-555555555555"));
            var expected = new DateTimeOffset(
                2026,
                9,
                9,
                10,
                0,
                0,
                TimeSpan.Zero);

            Assert.Equal(expected, account.LastAppliedBalanceObservationAt);
            Assert.Equal(expected, account.LastAppliedPositionsObservationAt);
        }
    }
}
