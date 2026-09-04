using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class TradeSystemDbContextPostgreSqlTests : IAsyncLifetime
{
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
}
