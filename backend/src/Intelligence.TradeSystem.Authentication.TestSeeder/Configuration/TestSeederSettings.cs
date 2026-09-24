namespace Intelligence.TradeSystem.Authentication.TestSeeder.Configuration;

public sealed record TestSeederSettings(
    string ConnectionString,
    string Username,
    string Password,
    string ClientId,
    string RedirectUri)
{
    public static TestSeederSettings FromConfiguration(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TradeSystemIdentity")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__TradeSystemIdentity must be configured.");

        return new TestSeederSettings(
            connectionString,
            GetRequired(configuration, "TestSeeder:Username"),
            GetRequired(configuration, "TestSeeder:Password"),
            GetRequired(configuration, "TestSeeder:ClientId"),
            GetRequired(configuration, "TestSeeder:RedirectUri"));
    }

    private static string GetRequired(IConfiguration configuration, string key) =>
        configuration[key]
        ?? throw new InvalidOperationException($"{key} must be configured.");
}
