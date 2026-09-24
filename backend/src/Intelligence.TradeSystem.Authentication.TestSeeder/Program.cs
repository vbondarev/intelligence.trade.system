namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public static class Program
{
    public static async Task Main()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddAuthenticationTestSeeding(builder.Configuration);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<AuthenticationTestSeeder>();
        await seeder.SeedAsync();
    }
}
