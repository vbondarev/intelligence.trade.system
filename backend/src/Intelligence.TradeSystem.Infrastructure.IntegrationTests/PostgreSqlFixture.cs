using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

    public Task InitializeAsync() => postgres.StartAsync();

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    public TradeSystemDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options);
}

[CollectionDefinition("PostgreSql")]
public sealed class PostgreSqlTestGroup : ICollectionFixture<PostgreSqlFixture>;
