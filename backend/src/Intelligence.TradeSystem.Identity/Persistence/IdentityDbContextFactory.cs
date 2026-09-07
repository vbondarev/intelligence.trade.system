using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Intelligence.TradeSystem.Identity.Persistence;

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TradeSystemIdentity");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__TradeSystemIdentity before running EF Core migrations.");
        }

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name))
            .Options;

        return new IdentityDbContext(options);
    }
}
