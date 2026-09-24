namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddAuthenticationTestSeeder(builder.Configuration);

        using var host = builder.Build();
        await host.Services.SeedAuthenticationTestUserAsync();
    }
}
