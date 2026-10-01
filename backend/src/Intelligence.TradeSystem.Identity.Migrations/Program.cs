using Intelligence.TradeSystem.Identity.Persistence;

namespace Intelligence.TradeSystem.Identity.Migrations;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var connectionString = IdentityDesignTimeConnectionString.ReadRequiredFromEnvironment();

        await IdentityMigrationRunner.ApplyAsync(connectionString);
    }
}
