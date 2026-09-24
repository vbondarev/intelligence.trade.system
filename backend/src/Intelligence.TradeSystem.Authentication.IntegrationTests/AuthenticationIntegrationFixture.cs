using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Migrations;
using Intelligence.TradeSystem.Identity.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenIddict.EntityFrameworkCore.Models;
using Testcontainers.PostgreSql;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

public sealed class AuthenticationIntegrationFixture : IAsyncLifetime
{
    internal const string Issuer = "http://public-identity.test/";
    internal const string ConfiguredIssuer = "http://public-identity.test";
    internal const string MetadataAddress = "http://identity-internal.test/.well-known/openid-configuration";
    internal const string BackchannelBaseAddress = "http://identity-internal.test";
    internal const string Audience = "intelligence-trade-api";
    internal const string ClientId = "c05a-public-client";
    internal const string RedirectUri = "http://client.test/callback";
    internal const string Username = "integration-user";
    internal const string Password = "Integration-password-123";
    internal const string SecondUsername = "integration-user-b";
    internal const string SecondPassword = "Integration-password-456";
    internal const string CertificatePassword = "integration-certificate-password";
    private static readonly string CredentialProtectionKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("postgres")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    private string oldCertificatePath = string.Empty;
    private string newCertificatePath = string.Empty;

    internal string BusinessConnectionString { get; private set; } = string.Empty;
    internal string IdentityConnectionString { get; private set; } = string.Empty;
    internal string OldCertificatePath => oldCertificatePath;
    internal string NewCertificatePath => newCertificatePath;
    internal IdentityWebApplicationFactory IdentityFactory { get; private set; } = null!;
    internal ApiWebApplicationFactory ApiFactory { get; private set; } = null!;
    internal Guid UserId { get; private set; }
    internal Guid SecondUserId { get; private set; }

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await CreateDatabaseAsync("tradesystem");
        await CreateDatabaseAsync("tradesystem_identity");

        BusinessConnectionString = BuildDatabaseConnectionString("tradesystem");
        IdentityConnectionString = BuildDatabaseConnectionString("tradesystem_identity");

        await IdentityMigrationRunner.ApplyAsync(IdentityConnectionString);
        await using (var context = CreateBusinessContext())
        {
            await context.Database.MigrateAsync();
        }

        oldCertificatePath = CreateSigningCertificate(
            "old",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(1));
        newCertificatePath = CreateSigningCertificate(
            "new",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(2));

        IdentityFactory = new IdentityWebApplicationFactory(
            IdentityConnectionString,
            oldCertificatePath,
            newCertificatePath,
            CertificatePassword);
        _ = IdentityFactory.Server;
        await SeedIdentityAsync();

        ApiFactory = CreateApiFactory();
        _ = ApiFactory.Server;
    }

    public async Task DisposeAsync()
    {
        ApiFactory?.Dispose();
        IdentityFactory?.Dispose();

        DeleteCertificate(oldCertificatePath);
        DeleteCertificate(newCertificatePath);
        await postgres.DisposeAsync();
    }

    internal ApiWebApplicationFactory CreateApiFactory() =>
        new(
            IdentityFactory,
            BusinessConnectionString);

    internal IdentityDbContext CreateIdentityContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                IdentityConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name))
            .Options);

    internal TradeSystemDbContext CreateBusinessContext() =>
        new(new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                BusinessConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .Options);

    internal async Task ResetMutableStateAsync()
    {
        await using (var identityContext = CreateIdentityContext())
        {
            await identityContext.OpenIddictTokens.ExecuteDeleteAsync();
            await identityContext.OpenIddictAuthorizations.ExecuteDeleteAsync();
        }

        using (var scope = IdentityFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            foreach (var username in new[] { Username, SecondUsername })
            {
                var user = await userManager.FindByNameAsync(username);
                if (user is null)
                {
                    continue;
                }

                await userManager.SetLockoutEndDateAsync(user, null);
                await userManager.ResetAccessFailedCountAsync(user);
            }
        }

        await using (var businessContext = CreateBusinessContext())
        {
            await businessContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM exchange_accounts WHERE user_id IN ({UserId}, {SecondUserId})");
        }
    }

    private async Task SeedIdentityAsync()
    {
        using var scope = IdentityFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Username };
        var userResult = await userManager.CreateAsync(user, Password);
        userResult.Succeeded.Should().BeTrue(
            string.Join(", ", userResult.Errors.Select(error => error.Description)));
        UserId = user.Id;

        var secondUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = SecondUsername };
        var secondUserResult = await userManager.CreateAsync(secondUser, SecondPassword);
        secondUserResult.Succeeded.Should().BeTrue(
            string.Join(", ", secondUserResult.Errors.Select(error => error.Description)));
        SecondUserId = secondUser.Id;

        var applicationManager = scope.ServiceProvider
            .GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();
        await applicationManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = ClientId,
            DisplayName = "C-05A integration test client",
            RedirectUris = { new Uri(RedirectUri) },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OpenId,
                Permissions.Prefixes.Scope + StartupExtensions.ApiScope
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        });
    }

    private async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
    }

    private string BuildDatabaseConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;

    private static string CreateSigningCertificate(string name, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Identity Integration Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var path = Path.Combine(Path.GetTempPath(), $"identity-c05a-{name}-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, CertificatePassword));
        return path;
    }

    private static void DeleteCertificate(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal sealed class IdentityWebApplicationFactory(
        string connectionString,
        string oldSigningCertificatePath,
        string newSigningCertificatePath,
        string signingCertificatePassword)
        : WebApplicationFactory<IdentityApplicationMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TradeSystemIdentity", connectionString);
            builder.UseSetting("Identity:Issuer", Issuer);
            builder.UseSetting("Identity:SigningCertificates:0:Path", oldSigningCertificatePath);
            builder.UseSetting("Identity:SigningCertificates:0:Password", signingCertificatePassword);
            builder.UseSetting("Identity:SigningCertificates:1:Path", newSigningCertificatePath);
            builder.UseSetting("Identity:SigningCertificates:1:Password", signingCertificatePassword);
            builder.UseSetting("Identity:AccessTokenLifetime", "00:15:00");
            builder.UseSetting("Identity:MaxFailedAccessAttempts", "3");
            builder.UseSetting("Identity:DefaultLockoutTimeSpan", "00:00:30");
            builder.UseSetting("Identity:AllowedForNewUsers", "true");
        }
    }

    internal sealed class ApiWebApplicationFactory(
        IdentityWebApplicationFactory identityFactory,
        string businessConnectionString)
        : WebApplicationFactory<Intelligence.TradeSystem.Api.Program>
    {
        private readonly RecordingHandler recordingHandler = new(identityFactory.Server.CreateHandler());

        internal Uri[] RequestedBackchannelUris => recordingHandler.RequestedUris;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Issuer", ConfiguredIssuer);
            builder.UseSetting("Authentication:MetadataAddress", MetadataAddress);
            builder.UseSetting("Authentication:BackchannelBaseAddress", BackchannelBaseAddress);
            builder.UseSetting("Authentication:Audience", Audience);
            builder.UseSetting("ConnectionStrings:TradeSystem", businessConnectionString);
            builder.UseSetting("CredentialProtection:ActiveKeyId", "integration-v1");
            builder.UseSetting("CredentialProtection:Keys:integration-v1", CredentialProtectionKey);
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(TestOnlyProtectedController).Assembly)
                    .AddApplicationPart(typeof(TestOnlyUserAccountController).Assembly);
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.BackchannelHttpHandler = new PublicIssuerBackchannelHandler(
                        new Uri(Issuer),
                        new Uri(BackchannelBaseAddress),
                        recordingHandler);
                });
            });
        }
    }

    internal sealed class RecordingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private readonly List<Uri> requestedUris = [];

        internal Uri[] RequestedUris
        {
            get
            {
                lock (requestedUris)
                {
                    return requestedUris.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                lock (requestedUris)
                {
                    requestedUris.Add(request.RequestUri);
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
