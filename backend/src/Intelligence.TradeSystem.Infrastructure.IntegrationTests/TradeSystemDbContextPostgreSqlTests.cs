using System.Globalization;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class TradeSystemDbContextPostgreSqlTests : IAsyncLifetime
{
    private const string C06Migration = "20260907131212_AddUserIsolationIndexes";
    private const string D04Migration = "20260909092129_AddExchangeAccountSyncWatermark";
    private const string BeforeRecommendationStabilityMigration =
        "20260913192851_PersistRecommendationContinuationMetadata";
    private const string BeforeProviderIdentityMigration =
        "20260914220611_PersistRecommendationStabilityState";
    private const string ProviderIdentityMigration =
        "20260919233157_AddExchangeAccountProviderIdentity";
    private const string LatestAssessmentIndexMigration =
        "20260921170115_AddPositionAssessmentLatestIndex";

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
        Assert.Equal(
            "timestamp with time zone",
            await dbContext.Database.SqlQueryRaw<string>(
                """
                SELECT data_type AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'recommendations'
                  AND column_name = 'next_evaluation_at'
                """)
                .SingleAsync());
        Assert.Equal(
            "jsonb",
            await dbContext.Database.SqlQueryRaw<string>(
                """
                SELECT data_type AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'recommendations'
                  AND column_name = 'continuation_context_json'
                """)
                .SingleAsync());
        Assert.Equal(
            "YES",
            await dbContext.Database.SqlQueryRaw<string>(
                """
                SELECT is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'recommendations'
                  AND column_name = 'next_evaluation_at'
                """)
                .SingleAsync());
        Assert.Equal(
            "YES",
            await dbContext.Database.SqlQueryRaw<string>(
                """
                SELECT is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'recommendations'
                  AND column_name = 'continuation_context_json'
                """)
                .SingleAsync());
        Assert.True(
            await dbContext.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'public'
                          AND table_name = 'recommendation_stability_states')
                    AS "Value"
                    """)
                .SingleAsync());
        Assert.True(
            await dbContext.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'public'
                          AND indexname = 'ux_recommendations_current_position')
                    AS "Value"
                    """)
                .SingleAsync());
        Assert.True(
            await dbContext.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT condeferrable AS "Value"
                    FROM pg_constraint
                    WHERE conname = 'fk_recommendations_successor'
                    """)
                .SingleAsync());

        await dbContext.Database.MigrateAsync("0");
        Assert.Empty(await dbContext.Database.GetAppliedMigrationsAsync());

        await dbContext.Database.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Stability_migration_rejects_legacy_duplicate_current_rows_without_mutation()
    {
        await using var dbContext = CreateMigrationContext();
        await dbContext.Database.MigrateAsync(BeforeRecommendationStabilityMigration);
        await SeedLegacyRecommendationsAsync(dbContext, duplicateCurrentRows: true);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => dbContext.Database.MigrateAsync());

        Assert.Contains("duplicate Active/Acknowledged recommendations", exception.Message);
        Assert.Equal(
            2,
            await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::integer AS "Value"
                FROM recommendations
                WHERE position_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
                """)
                .SingleAsync());
        Assert.False(
            await dbContext.Database.SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name = 'recommendation_stability_states')
                AS "Value"
                """)
                .SingleAsync());
    }

    [Fact]
    public async Task Stability_migration_accepts_legacy_data_with_one_current_row()
    {
        await using var dbContext = CreateMigrationContext();
        await dbContext.Database.MigrateAsync(BeforeRecommendationStabilityMigration);
        await SeedLegacyRecommendationsAsync(dbContext, duplicateCurrentRows: false);

        await dbContext.Database.MigrateAsync(BeforeProviderIdentityMigration);

        Assert.Equal(
            [ProviderIdentityMigration, LatestAssessmentIndexMigration],
            (await dbContext.Database.GetPendingMigrationsAsync()).ToArray());
        Assert.True(
            await dbContext.Database.SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND indexname = 'ux_recommendations_current_position')
                AS "Value"
                """)
                .SingleAsync());
    }

    private TradeSystemDbContext CreateMigrationContext() =>
        new(new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options);

    private static async Task SeedLegacyRecommendationsAsync(
        TradeSystemDbContext dbContext,
        bool duplicateCurrentRows)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO exchange_accounts (
                exchange_account_id, user_id, exchange_id, connection_status,
                capabilities, last_synced_at, last_error, version)
            VALUES (
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                'cccccccc-cccc-cccc-cccc-cccccccccccc',
                'Bybit', 'Connected', 3, NULL, NULL, 1);

            INSERT INTO positions (
                position_id, exchange_account_id, instrument_id, position_side,
                position_idx, market_category, size, first_detected_at,
                last_observed_at, tracking_state, version)
            VALUES (
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                'BTCUSDT', 'Long', 0, 'Linear', 1,
                '2026-09-15T10:00:00Z', '2026-09-15T10:00:00Z',
                'Active', 1);

            INSERT INTO position_assessments (
                position_assessment_id, position_id, exchange_account_id,
                instrument_id, position_observed_at, portfolio_calculated_at,
                market_captured_at, rule_version,
                base_policy_configuration_version, base_policy_configuration_hash,
                policy_configuration_version, policy_configuration_hash,
                result_json, created_at, valid_until, portfolio_risk_decision)
            VALUES (
                'dddddddd-dddd-dddd-dddd-dddddddddddd',
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                'BTCUSDT', '2026-09-15T10:00:00Z', '2026-09-15T10:00:00Z',
                '2026-09-15T10:00:00Z', 'assessment-v1',
                'policy-v1', 'legacy-hash',
                'policy-v1', 'legacy-hash',
                NULL, '2026-09-15T10:01:00Z', '2026-09-15T11:00:00Z',
                'Blocked');

            INSERT INTO recommendations (
                recommendation_id, position_assessment_id, position_id,
                recommended_action, add_decision, policy_version,
                created_at, valid_until, status, version)
            VALUES
                ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
                 'dddddddd-dddd-dddd-dddd-dddddddddddd',
                 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                 'Watch', 'DoNotAdd', 'policy-v1',
                 '2026-09-15T10:02:00Z', '2026-09-15T11:00:00Z',
                 'Active', 1)
                {(duplicateCurrentRows ? ",\n                ('ffffffff-ffff-ffff-ffff-ffffffffffff',\n                 'dddddddd-dddd-dddd-dddd-dddddddddddd',\n                 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',\n                 'Watch', 'DoNotAdd', 'policy-v1',\n                 '2026-09-15T10:03:00Z', '2026-09-15T11:00:00Z',\n                 'Acknowledged', 1)" : string.Empty)};
            """;
        await command.ExecuteNonQueryAsync();
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
            await dbContext.Database.MigrateAsync(BeforeProviderIdentityMigration);

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
            await dbContext.Database.MigrateAsync(BeforeProviderIdentityMigration);

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = """
                SELECT user_id, last_applied_balance_observation_at,
                    last_applied_positions_observation_at
                FROM exchange_accounts
                WHERE exchange_account_id = '33333333-3333-3333-3333-333333333333'
                """;
            await using (var reader = await selectCommand.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    reader.GetGuid(0));
                Assert.True(reader.IsDBNull(1));
                Assert.True(reader.IsDBNull(2));
            }

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = """
                DELETE FROM exchange_accounts
                WHERE exchange_account_id = '33333333-3333-3333-3333-333333333333'
                """;
            await deleteCommand.ExecuteNonQueryAsync();
            await dbContext.Database.MigrateAsync();

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
            await dbContext.Database.MigrateAsync(BeforeProviderIdentityMigration);

            var expected = new DateTimeOffset(
                2026,
                9,
                9,
                10,
                0,
                0,
                TimeSpan.Zero);

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = """
                SELECT last_applied_balance_observation_at,
                    last_applied_positions_observation_at
                FROM exchange_accounts
                WHERE exchange_account_id = '55555555-5555-5555-5555-555555555555'
                """;
            await using (var reader = await selectCommand.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(expected, reader.GetFieldValue<DateTimeOffset>(0));
                Assert.Equal(expected, reader.GetFieldValue<DateTimeOffset>(1));
            }

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = """
                DELETE FROM exchange_accounts
                WHERE exchange_account_id = '55555555-5555-5555-5555-555555555555'
                """;
            await deleteCommand.ExecuteNonQueryAsync();
            await dbContext.Database.MigrateAsync();
        }
    }
}
