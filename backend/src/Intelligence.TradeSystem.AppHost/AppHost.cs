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
        var credentialProtectionKey = builder.AddParameter(
            "tradeCredentialKey",
            secret: true);

        var identityMigrations = builder
            .AddProject<Projects.Intelligence_TradeSystem_Identity_Migrations>("identity-migrations")
            .WithReference(identityDatabase)
            .WaitFor(identityDatabase);

        var identity = builder
            .AddProject<Projects.Intelligence_TradeSystem_Identity>("identity")
            .WithReference(identityDatabase)
            .WaitForCompletion(identityMigrations)
            .WithEndpoint("http", endpoint =>
            {
                endpoint.Port = 8081;
                endpoint.TargetPort = 8080;
            })
            .WithExternalHttpEndpoints();
        var identityEndpoint = identity.GetEndpoint("http");
        identity.WithEnvironment("Identity__Issuer", "http://localhost:8081");

        builder
            .AddProject<Projects.Intelligence_TradeSystem_Api>("api")
            .WithReference(businessDatabase)
            .WithReference(identity)
            .WaitFor(businessDatabase)
            .WaitFor(identity)
            .WithEnvironment("Authentication__Issuer", "http://localhost:8081")
            .WithEnvironment("Authentication__MetadataAddress", identityEndpoint)
            .WithEnvironment("Authentication__BackchannelBaseAddress", identityEndpoint)
            .WithEnvironment("CredentialProtection__ActiveKeyId", "local_v1")
            .WithEnvironment("CredentialProtection__Keys__local_v1", credentialProtectionKey)
            .WithExternalHttpEndpoints()
            .WithUrl("/swagger", "Swagger");

        builder.Build().Run();
    }
}
