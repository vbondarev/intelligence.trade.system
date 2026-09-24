using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    public TradeSystemDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options);
}

[CollectionDefinition("PostgreSql-A")]
public sealed class PostgreSqlTestGroupA : ICollectionFixture<PostgreSqlFixture>;

[CollectionDefinition("PostgreSql-B")]
public sealed class PostgreSqlTestGroupB : ICollectionFixture<PostgreSqlFixture>;

public sealed class PostgreSqlMigrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("postgres")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    public string ConnectionString => postgres.GetConnectionString();

    public Task InitializeAsync() => postgres.StartAsync();

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    public string BuildDatabaseConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

    public async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DropDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("PostgreSql-Migrations", DisableParallelization = true)]
public sealed class PostgreSqlMigrationTestGroup : ICollectionFixture<PostgreSqlMigrationFixture>;
