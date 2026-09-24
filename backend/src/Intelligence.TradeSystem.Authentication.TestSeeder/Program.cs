namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddAuthenticationTestSeeding(builder.Configuration);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<AuthenticationTestSeeder>();
        await seeder.SeedAsync();
    }
}
