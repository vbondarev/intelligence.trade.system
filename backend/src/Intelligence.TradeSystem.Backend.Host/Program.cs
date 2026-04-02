using Intelligence.TradeSystem.Exchanges;

namespace Intelligence.TradeSystem.Backend.Host;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Services.AddHostedService<Worker>();
        builder.Services.AddBybitExchange();

        var host = builder.Build();
        host.Run();
    }
}
