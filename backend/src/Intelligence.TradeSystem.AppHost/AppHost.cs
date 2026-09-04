namespace Intelligence.TradeSystem.AppHost;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        var database = builder
            .AddPostgres("postgres")
            .WithDataVolume()
            .AddDatabase("TradeSystem");

        builder
            .AddProject<Projects.Intelligence_TradeSystem_Api>("api")
            .WithReference(database)
            .WaitFor(database)
            .WithExternalHttpEndpoints()
            .WithUrl("/swagger", "Swagger");

        builder.Build().Run();
    }
}
