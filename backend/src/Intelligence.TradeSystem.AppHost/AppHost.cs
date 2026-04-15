namespace Intelligence.TradeSystem.AppHost;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        builder
            .AddProject<Projects.Intelligence_TradeSystem_Api>("api")
            .WithExternalHttpEndpoints()
            .WithUrl("/swagger", "Swagger");

        builder.Build().Run();
    }
}
