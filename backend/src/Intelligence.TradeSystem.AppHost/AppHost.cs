namespace Intelligence.TradeSystem.AppHost;

public static class Program
{
    private const string PublicIdentityIssuer = "http://localhost:8081";
    private const string PublicWebOrigin = "http://localhost:8082";

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
        var webBffClientSecret = builder.AddParameter(
            "tradeWebBffClientSecret",
            secret: true);
        var webDevelopmentPassword = builder.AddParameter(
            "tradeWebDevelopmentPassword",
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
        identity
            .WithEnvironment("Identity__Issuer", PublicIdentityIssuer)
            .WithEnvironment("Identity__WebBffClient__Enabled", "true")
            .WithEnvironment("Identity__WebBffClient__ClientId", "trade-web-bff")
            .WithEnvironment("Identity__WebBffClient__ClientSecret", webBffClientSecret)
            .WithEnvironment("Identity__WebBffClient__RedirectUris__0", $"{PublicWebOrigin}/signin-oidc")
            .WithEnvironment(
                "Identity__WebBffClient__PostLogoutRedirectUris__0",
                $"{PublicWebOrigin}/signout-callback-oidc")
            .WithEnvironment("Identity__DevelopmentUser__Enabled", "true")
            .WithEnvironment("Identity__DevelopmentUser__Username", "trade-dev-user")
            .WithEnvironment("Identity__DevelopmentUser__Password", webDevelopmentPassword);

        var api = builder
            .AddProject<Projects.Intelligence_TradeSystem_Api>("api")
            .WithReference(businessDatabase)
            .WithReference(identity)
            .WaitFor(businessDatabase)
            .WaitFor(identity)
            .WithEnvironment("Authentication__Issuer", PublicIdentityIssuer)
            .WithEnvironment("Authentication__MetadataAddress", identityEndpoint)
            .WithEnvironment("Authentication__BackchannelBaseAddress", identityEndpoint)
            .WithEnvironment("CredentialProtection__ActiveKeyId", "local_v1")
            .WithEnvironment("CredentialProtection__Keys__local_v1", credentialProtectionKey)
            .WithExternalHttpEndpoints()
            .WithUrl("/swagger", "Swagger");

        builder
            .AddProject<Projects.Intelligence_TradeSystem_Web>("web")
            .WithReference(identity)
            .WithReference(api)
            .WaitFor(identity)
            .WaitFor(api)
            .WithEndpoint("http", endpoint =>
            {
                endpoint.Port = 8082;
                endpoint.TargetPort = 8080;
            })
            .WithEnvironment("Web__Oidc__Authority", PublicIdentityIssuer)
            .WithEnvironment(
                "Web__Oidc__MetadataAddress",
                ReferenceExpression.Create($"{identityEndpoint}/.well-known/openid-configuration"))
            .WithEnvironment("Web__Oidc__BackchannelBaseAddress", identityEndpoint)
            .WithEnvironment("Web__Oidc__ClientId", "trade-web-bff")
            .WithEnvironment("Web__Oidc__ClientSecret", webBffClientSecret)
            .WithEnvironment("Web__Api__BaseAddress", api.GetEndpoint("http"))
            .WithExternalHttpEndpoints();

        builder.Build().Run();
    }
}
