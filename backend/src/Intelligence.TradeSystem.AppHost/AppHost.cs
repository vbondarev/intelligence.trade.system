namespace Intelligence.TradeSystem.AppHost;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        var database = builder
            .AddPostgres("postgres")
            .WithDataVolume();
        var businessDatabase = database.AddDatabase("TradeSystem");
        var identityDatabase = database.AddDatabase("TradeSystemIdentity");

        var identity = builder
            .AddProject<Projects.Intelligence_TradeSystem_Identity>("identity")
            .WithReference(identityDatabase)
            .WaitFor(identityDatabase)
            .WithExternalHttpEndpoints();
        var identityEndpoint = identity.GetEndpoint("http");
        identity.WithEnvironment("Identity__Issuer", identityEndpoint);

        builder
            .AddProject<Projects.Intelligence_TradeSystem_Api>("api")
            .WithReference(businessDatabase)
            .WithReference(identity)
            .WaitFor(businessDatabase)
            .WaitFor(identity)
            .WithEnvironment("Authentication__Authority", identityEndpoint)
            .WithExternalHttpEndpoints()
            .WithUrl("/swagger", "Swagger");

        builder.Build().Run();
    }
}
