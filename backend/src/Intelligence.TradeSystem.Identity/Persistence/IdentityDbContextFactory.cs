using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Intelligence.TradeSystem.Identity.Persistence;

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = IdentityDesignTimeConnectionString.ReadRequiredFromEnvironment();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name))
            .Options;

        return new IdentityDbContext(options);
    }
}
