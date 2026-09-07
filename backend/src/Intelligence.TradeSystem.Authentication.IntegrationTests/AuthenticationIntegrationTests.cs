using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Intelligence.TradeSystem.Api;
using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

public sealed class AuthenticationIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string Issuer = "http://identity.test/";
    private const string Audience = "intelligence-trade-api";
    private const string ClientId = "c05a-public-client";
    private const string RedirectUri = "http://client.test/callback";
    private const string Username = "integration-user";
    private const string Password = "Integration-password-123";
    private const string CertificatePassword = "integration-certificate-password";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem_identity")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    private string certificatePath = string.Empty;
    private IdentityWebApplicationFactory identityFactory = null!;
    private ApiWebApplicationFactory apiFactory = null!;
    private Guid userId;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        certificatePath = CreateSigningCertificate();
        await MigrateIdentityDatabaseAsync();

        identityFactory = new IdentityWebApplicationFactory(
            postgres.GetConnectionString(),
            certificatePath,
            CertificatePassword);
        _ = identityFactory.Server;
        await SeedIdentityAsync();

        apiFactory = new ApiWebApplicationFactory(identityFactory);
        _ = apiFactory.Server;
    }

    public async Task DisposeAsync()
    {
        apiFactory.Dispose();
        identityFactory.Dispose();
        if (File.Exists(certificatePath))
        {
            File.Delete(certificatePath);
        }

        await postgres.DisposeAsync();
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task Identity_migrations_create_auth_schema_without_pending_migrations()
    {
        await using var context = CreateIdentityContext();

        (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle(
            migration => migration.Contains("InitialIdentityAuthorization", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        var tables = await ReadTableNamesAsync(context);
        tables.Should().Contain([
            "AspNetUsers",
            "OpenIddictApplications",
            "OpenIddictAuthorizations",
            "OpenIddictScopes",
            "OpenIddictTokens"
        ]);
        tables.Should().NotContain([
            "exchange_accounts",
            "positions",
            "portfolio_states",
            "recommendations"
        ]);

        (await ReadColumnDataTypeAsync(context, "AspNetUsers", "Id"))
            .Should().Be("uuid");
        (await context.Users.SingleAsync(user => user.Id == userId)).Id
            .Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Discovery_and_jwks_are_published_by_the_identity_host()
    {
        using var client = CreateIdentityClient();

        var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        discoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var discovery = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());

        discovery.RootElement.GetProperty("issuer").GetString().Should().Be(Issuer);
        var jwksUri = discovery.RootElement.GetProperty("jwks_uri").GetString();
        jwksUri.Should().NotBeNullOrWhiteSpace();

        var jwksResponse = await client.GetAsync(new Uri(new Uri(Issuer), jwksUri!).PathAndQuery);
        jwksResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var jwks = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync());
        jwks.RootElement.GetProperty("keys").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Authorization_code_pkce_issues_a_signed_user_token()
    {
        var token = await IssueAccessTokenAsync();
        var segments = token.AccessToken.Split('.');
        segments.Should().HaveCount(3);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        jwt.Subject.Should().Be(userId.ToString());
        Guid.Parse(jwt.Subject).Should().NotBe(Guid.Empty);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Audiences.Should().Contain(Audience);
        jwt.Claims.Single(claim => claim.Type == "scope").Value.Should().Contain("trade.api");
        jwt.ValidTo.Should().BeAfter(DateTime.UtcNow);
        jwt.Header.Alg.Should().NotBe(SecurityAlgorithms.None);
    }

    [Fact]
    public async Task Login_without_antiforgery_token_is_rejected()
    {
        using var client = CreateIdentityClient();

        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = Username,
                ["password"] = Password,
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorization_request_without_pkce_challenge_is_rejected()
    {
        using var client = CreateIdentityClient();
        var response = await client.GetAsync(
            "/connect/authorize?client_id=c05a-public-client"
            + "&redirect_uri=http%3A%2F%2Fclient.test%2Fcallback"
            + "&response_type=code&scope=openid%20trade.api");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorization_request_with_plain_pkce_is_rejected()
    {
        using var client = CreateIdentityClient();
        var response = await client.GetAsync(
            "/connect/authorize?client_id=c05a-public-client"
            + "&redirect_uri=http%3A%2F%2Fclient.test%2Fcallback"
            + "&response_type=code&scope=openid%20trade.api"
            + "&code_challenge=plain-challenge&code_challenge_method=plain");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Token_request_with_wrong_pkce_verifier_is_rejected()
    {
        var authorization = await IssueAuthorizationCodeAsync();
        using var client = CreateIdentityClient();
        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "authorization_code",
                ["code"] = authorization.Code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = Base64Url(RandomNumberGenerator.GetBytes(32)),
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Real_openiddict_token_is_accepted_by_api_and_preserves_subject()
    {
        var token = await IssueAccessTokenAsync();
        using var client = apiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/test-only/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("subject").GetString().Should().Be(userId.ToString());
    }

    [Fact]
    public async Task Jwt_bearer_rejects_missing_invalid_issuer_invalid_audience_and_expired_tokens()
    {
        using var client = apiFactory.CreateClient();

        (await client.GetAsync("/test-only/protected")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var realToken = await IssueAccessTokenAsync();
        var invalidSignature = realToken.AccessToken[..^1] + (realToken.AccessToken[^1] == 'a' ? "b" : "a");
        (await GetWithTokenAsync(client, invalidSignature)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrongIssuer = CreateSignedToken("http://wrong-issuer.test", Audience, DateTime.UtcNow.AddMinutes(5));
        (await GetWithTokenAsync(client, wrongIssuer)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrongAudience = CreateSignedToken(Issuer, "wrong-audience", DateTime.UtcNow.AddMinutes(5));
        (await GetWithTokenAsync(client, wrongAudience)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var expired = CreateSignedToken(Issuer, Audience, DateTime.UtcNow.AddMinutes(-5));
        (await GetWithTokenAsync(client, expired)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_token_without_trade_scope_is_forbidden()
    {
        using var client = apiFactory.CreateClient();
        var token = CreateSignedToken(Issuer, Audience, DateTime.UtcNow.AddMinutes(5), "openid");

        var response = await GetWithTokenAsync(client, token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Public_api_endpoints_remain_anonymous()
    {
        using var client = apiFactory.CreateClient();

        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/alive")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(string AccessToken, string CodeVerifier)> IssueAccessTokenAsync()
    {
        var authorization = await IssueAuthorizationCodeAsync();
        using var client = CreateIdentityClient();
        var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "authorization_code",
                ["code"] = authorization.Code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = authorization.CodeVerifier,
            }));
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenBody = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

        return (
            tokenBody.RootElement.GetProperty("access_token").GetString()!,
            authorization.CodeVerifier);
    }

    private async Task<(string Code, string CodeVerifier)> IssueAuthorizationCodeAsync()
    {
        using var client = CreateIdentityClient();
        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var authorizationQuery = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid trade.api",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "integration-state",
        };
        var authorizationUri = "/connect/authorize?" + string.Join(
            "&",
            authorizationQuery.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var authorizationResponse = await client.GetAsync(authorizationUri);
        authorizationResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loginUri = authorizationResponse.Headers.Location!.PathAndQuery;

        var loginResponse = await client.GetAsync(loginUri);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginHtml = await loginResponse.Content.ReadAsStringAsync();
        var antiforgeryToken = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        antiforgeryToken.Should().NotBeNullOrWhiteSpace();

        var loginQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            new Uri(new Uri(Issuer), loginUri).Query);
        var returnUrl = loginQuery["returnUrl"].ToString();
        var loginPost = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["returnUrl"] = returnUrl,
                ["username"] = Username,
                ["password"] = Password,
            }));
        loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var authorizationLocation = loginPost.Headers.Location!;
        var completedAuthorization = await client.GetAsync(
            authorizationLocation.IsAbsoluteUri
                ? authorizationLocation.PathAndQuery
                : authorizationLocation.OriginalString);
        completedAuthorization.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var callback = completedAuthorization.Headers.Location!;
        var callbackQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(callback.Query);
        var code = callbackQuery["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();
        callbackQuery["state"].ToString().Should().Be("integration-state");

        return (code, codeVerifier);
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test-only/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private string CreateSignedToken(
        string issuer,
        string audience,
        DateTime expires,
        string scope = "trade.api")
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new("sub", userId.ToString()),
                new("scope", scope),
            ]),
            Expires = expires,
            NotBefore = expires < DateTime.UtcNow
                ? expires.AddMinutes(-10)
                : DateTime.UtcNow.AddMinutes(-1),
            SigningCredentials = new SigningCredentials(
                new X509SecurityKey(certificate),
                SecurityAlgorithms.RsaSha256),
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private HttpClient CreateIdentityClient() =>
        identityFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private async Task MigrateIdentityDatabaseAsync()
    {
        await using var context = CreateIdentityContext();
        await context.Database.MigrateAsync();
    }

    private IdentityDbContext CreateIdentityContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name))
            .Options);

    private async Task SeedIdentityAsync()
    {
        using var scope = identityFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = Username,
        };
        var userResult = await userManager.CreateAsync(user, Password);
        userResult.Succeeded.Should().BeTrue(string.Join(", ", userResult.Errors.Select(error => error.Description)));
        userId = user.Id;

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
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
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        });
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private string CreateSigningCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Identity Integration Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(2));
        var path = Path.Combine(Path.GetTempPath(), $"identity-c05a-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, CertificatePassword));
        return path;
    }

    private static async Task<string[]> ReadTableNamesAsync(IdentityDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
            """;
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables.ToArray();
    }

    private static async Task<string> ReadColumnDataTypeAsync(
        IdentityDbContext context,
        string tableName,
        string columnName)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table
              AND column_name = @column
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "table";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);
        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "column";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"Column {tableName}.{columnName} was not found."));
    }

    private sealed class IdentityWebApplicationFactory(
        string connectionString,
        string signingCertificatePath,
        string signingCertificatePassword)
        : WebApplicationFactory<Intelligence.TradeSystem.Identity.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TradeSystemIdentity", connectionString);
            builder.UseSetting("Identity:Issuer", Issuer);
            builder.UseSetting("Identity:SigningCertificatePath", signingCertificatePath);
            builder.UseSetting("Identity:SigningCertificatePassword", signingCertificatePassword);
        }
    }

    private sealed class ApiWebApplicationFactory(IdentityWebApplicationFactory identityFactory)
        : WebApplicationFactory<Intelligence.TradeSystem.Api.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Authority", Issuer);
            builder.UseSetting("Authentication:Audience", Audience);
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(TestOnlyProtectedController).Assembly);
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.BackchannelHttpHandler = identityFactory.Server.CreateHandler();
                });
            });
        }
    }
}
