using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed class TradeSystemDbContextFactory : IDesignTimeDbContextFactory<TradeSystemDbContext>
{
    public TradeSystemDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TradeSystem");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__TradeSystem before running EF Core migrations.");
        }

        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options;

        return new TradeSystemDbContext(options);
    }
}
