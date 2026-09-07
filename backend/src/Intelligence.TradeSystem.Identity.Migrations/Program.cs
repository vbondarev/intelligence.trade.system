namespace Intelligence.TradeSystem.Identity.Migrations;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TradeSystemIdentity")
                               ?? throw new InvalidOperationException(
                                   "Set ConnectionStrings__TradeSystemIdentity before running Identity migrations.");

        await IdentityMigrationRunner.ApplyAsync(connectionString);
    }
}
