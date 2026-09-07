using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Identity.Migrations;

public static class IdentityMigrationRunner
{
    public static async Task ApplyAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name))
            .Options;

        await using var context = new IdentityDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
