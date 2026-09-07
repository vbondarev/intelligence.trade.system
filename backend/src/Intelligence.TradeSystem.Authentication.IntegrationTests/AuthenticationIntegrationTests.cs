using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Migrations;
using Intelligence.TradeSystem.Identity.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
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
    private const string Issuer = "http://public-identity.test/";
    private const string ConfiguredIssuer = "http://public-identity.test";
    private const string MetadataAddress = "http://identity-internal.test/.well-known/openid-configuration";
    private const string BackchannelBaseAddress = "http://identity-internal.test";
    private const string Audience = "intelligence-trade-api";
    private const string ClientId = "c05a-public-client";
    private const string RedirectUri = "http://client.test/callback";
    private const string Username = "integration-user";
    private const string Password = "Integration-password-123";
    private const string SecondUsername = "integration-user-b";
    private const string SecondPassword = "Integration-password-456";
    private const string CertificatePassword = "integration-certificate-password";
    private const string PrincipalTypeClaim = "trade_principal_type";
    private const string UserPrincipalType = "user";
    private static readonly string CredentialProtectionKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem_identity")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();
    private readonly PostgreSqlContainer businessPostgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradesystem")
        .WithUsername("tradesystem")
        .WithPassword("tradesystem")
        .Build();

    private string oldCertificatePath = string.Empty;
    private string newCertificatePath = string.Empty;
    private IdentityWebApplicationFactory identityFactory = null!;
    private ApiWebApplicationFactory apiFactory = null!;
    private Guid userId;
    private Guid secondUserId;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await businessPostgres.StartAsync();
        oldCertificatePath = CreateSigningCertificate(
            "old",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(1));
        newCertificatePath = CreateSigningCertificate(
            "new",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(2));
        await IdentityMigrationRunner.ApplyAsync(postgres.GetConnectionString());
        await IdentityMigrationRunner.ApplyAsync(postgres.GetConnectionString());

        identityFactory = new IdentityWebApplicationFactory(
            postgres.GetConnectionString(),
            oldCertificatePath,
            newCertificatePath,
            CertificatePassword);
        _ = identityFactory.Server;
        await SeedIdentityAsync();
        await ApplyBusinessMigrationsAsync();

        apiFactory = new ApiWebApplicationFactory(
            identityFactory,
            businessPostgres.GetConnectionString());
        _ = apiFactory.Server;
    }

    public async Task DisposeAsync()
    {
        apiFactory.Dispose();
        identityFactory.Dispose();
        if (File.Exists(oldCertificatePath))
        {
            File.Delete(oldCertificatePath);
        }

        if (File.Exists(newCertificatePath))
        {
            File.Delete(newCertificatePath);
        }

        await postgres.DisposeAsync();
        await businessPostgres.DisposeAsync();
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
    public async Task Migration_runner_fails_when_database_is_unreachable()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            IdentityMigrationRunner.ApplyAsync(
                "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none;Timeout=1"));
    }

    [Fact]
    public async Task Discovery_and_jwks_are_published_by_the_identity_host()
    {
        using var client = CreateIdentityClient();

        var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        discoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var discovery = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());

        discovery.RootElement.GetProperty("issuer").GetString().Should().Be(Issuer);
        discovery.RootElement.GetProperty("authorization_endpoint").GetString()
            .Should().StartWith(Issuer);
        discovery.RootElement.GetProperty("token_endpoint").GetString()
            .Should().StartWith(Issuer);
        var jwksUri = discovery.RootElement.GetProperty("jwks_uri").GetString();
        jwksUri.Should().NotBeNullOrWhiteSpace();
        jwksUri.Should().StartWith(Issuer);
        discovery.RootElement.GetProperty("scopes_supported")
            .EnumerateArray()
            .Select(scope => scope.GetString())
            .Should()
            .Contain([Scopes.OpenId, StartupExtensions.ApiScope]);

        var jwksResponse = await client.GetAsync(new Uri(new Uri(Issuer), jwksUri!).PathAndQuery);
        jwksResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var jwks = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync());
        var keys = jwks.RootElement.GetProperty("keys").EnumerateArray().ToArray();
        keys.Should().HaveCountGreaterThan(1);
        using var oldCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            oldCertificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        using var newCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            newCertificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        keys.Select(key => key.GetProperty("kid").GetString())
            .Should()
            .Contain([oldCertificate.Thumbprint, newCertificate.Thumbprint]);
    }

    [Fact]
    public async Task Identity_starts_after_migrations_and_scope_seeding()
    {
        using var scope = identityFactory.Services.CreateScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        (await scopeManager.FindByNameAsync(StartupExtensions.ApiScope))
            .Should().NotBeNull();

        using var client = CreateIdentityClient();
        (await client.GetAsync("/.well-known/openid-configuration"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
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
        jwt.Claims.Single(claim => claim.Type == PrincipalTypeClaim).Value.Should().Be(UserPrincipalType);
        jwt.ValidTo.Should().BeAfter(DateTime.UtcNow);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(
            long.Parse(
                jwt.Claims.Single(claim => claim.Type == "iat").Value,
                CultureInfo.InvariantCulture)).UtcDateTime;
        (jwt.ValidTo - issuedAt).Should().BeCloseTo(
            TimeSpan.FromMinutes(15),
            TimeSpan.FromSeconds(30));
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
    public async Task Failed_passwords_activate_lockout_and_correct_password_is_rejected()
    {
        using var client = CreateIdentityClient();
        var invalidResponse = await SubmitLoginAsync(client, Username, "Wrong-password-123");
        var invalidBody = await invalidResponse.Content.ReadAsStringAsync();
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        for (var attempt = 1; attempt < 3; attempt++)
        {
            (await SubmitLoginAsync(client, Username, "Wrong-password-123"))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var scope = identityFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = await userManager.FindByNameAsync(Username);
            user.Should().NotBeNull();
            (await userManager.IsLockedOutAsync(user!)).Should().BeTrue();

            var lockedResponse = await SubmitLoginAsync(client, Username, Password);
            lockedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await lockedResponse.Content.ReadAsStringAsync()).Should().Be(invalidBody);

            var missingResponse = await SubmitLoginAsync(client, "missing-user", Password);
            (await missingResponse.Content.ReadAsStringAsync()).Should().Be(invalidBody);

            await userManager.SetLockoutEndDateAsync(user!, null);
            await userManager.ResetAccessFailedCountAsync(user!);
        }

        (await SubmitLoginAsync(client, Username, Password))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
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
    public async Task Real_openiddict_token_is_accepted_by_api_with_normalized_issuer_and_preserves_subject()
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
    public async Task Jwt_bearer_backchannel_routes_metadata_and_public_jwks_to_internal_host()
    {
        var token = await IssueAccessTokenAsync();
        using var client = apiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/test-only/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var requestedUris = apiFactory.RequestedBackchannelUris;
        requestedUris.Should().Contain(uri => uri.AbsoluteUri == MetadataAddress);
        requestedUris.Should().Contain(uri => uri.AbsoluteUri == $"{BackchannelBaseAddress}/.well-known/jwks");
        requestedUris.Should().NotContain(uri => uri.AbsoluteUri == $"{Issuer}.well-known/jwks");
    }

    [Fact]
    public async Task Auth_me_returns_only_the_current_user_id()
    {
        var token = await IssueAccessTokenAsync();
        using var client = apiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("userId").GetGuid().Should().Be(userId);
        body.RootElement.GetProperty("subject").GetString().Should().Be(userId.ToString());
        body.RootElement.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should()
            .BeEquivalentTo(["userId", "subject", "authenticated"]);
    }

    [Fact]
    public async Task Real_authorization_code_tokens_isolate_known_business_account_ids()
    {
        var accountA = CreateBusinessAccount(userId);
        var accountB = CreateBusinessAccount(secondUserId);
        await using (var context = await CreateBusinessContextAsync())
        {
            var repository = new ExchangeAccountRepository(context);
            await repository.SaveAsync(accountA.UserId, accountA, expectedVersion: null);
            await repository.SaveAsync(accountB.UserId, accountB, expectedVersion: null);
        }

        var tokenA = await IssueAccessTokenAsync(Username, Password);
        var tokenB = await IssueAccessTokenAsync(SecondUsername, SecondPassword);

        var ownA = await GetAccountWithTokenAsync(accountA.Id, tokenA.AccessToken);
        ownA.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await ownA.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("accountId").GetGuid().Should().Be(accountA.Id.Value);
            body.RootElement.GetProperty("userId").GetGuid().Should().Be(userId);
        }

        var ownB = await GetAccountWithTokenAsync(accountB.Id, tokenB.AccessToken);
        ownB.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetAccountWithTokenAsync(accountB.Id, tokenA.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetAccountWithTokenAsync(accountA.Id, tokenB.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("machine")]
    public async Task TradeUser_rejects_a_non_user_token_while_TradeApi_accepts_it(
        string? principalType)
    {
        using var client = apiFactory.CreateClient();
        var token = CreateSignedToken(
            Issuer,
            Audience,
            DateTime.UtcNow.AddMinutes(5),
            principalType: principalType);

        (await GetWithTokenAsync(client, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task TradeUser_rejects_a_user_marker_with_an_invalid_subject(string subject)
    {
        using var client = apiFactory.CreateClient();
        var token = CreateSignedToken(
            Issuer,
            Audience,
            DateTime.UtcNow.AddMinutes(5),
            subject: subject,
            principalType: UserPrincipalType);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auth_me_without_a_token_is_unauthorized()
    {
        using var client = apiFactory.CreateClient();

        (await client.GetAsync("/api/v1/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Openiddict_selects_the_valid_certificate_with_the_furthest_expiration_and_old_key_remains_accepted()
    {
        var issuedToken = await IssueAccessTokenAsync();
        var issuedJwt = new JwtSecurityTokenHandler().ReadJwtToken(issuedToken.AccessToken);
        using var oldCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            oldCertificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        using var currentCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            newCertificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        currentCertificate.NotAfter.Should().BeAfter(oldCertificate.NotAfter);
        issuedJwt.Header.Kid.Should().Be(currentCertificate.Thumbprint);

        using var client = apiFactory.CreateClient();
        var oldToken = CreateSignedToken(
            Issuer,
            Audience,
            DateTime.UtcNow.AddMinutes(5),
            "trade.api",
            oldCertificatePath);

        var response = await GetWithTokenAsync(client, oldToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    private async Task<(string AccessToken, string CodeVerifier)> IssueAccessTokenAsync(
        string username = Username,
        string password = Password)
    {
        var authorization = await IssueAuthorizationCodeAsync(username, password);
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

    private async Task<(string Code, string CodeVerifier)> IssueAuthorizationCodeAsync(
        string username = Username,
        string password = Password)
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
                ["username"] = username,
                ["password"] = password,
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

    private async Task<HttpResponseMessage> GetAccountWithTokenAsync(
        ExchangeAccountId accountId,
        string token)
    {
        using var client = apiFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/test-only/accounts/{accountId.Value:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SubmitLoginAsync(
        HttpClient client,
        string username,
        string password)
    {
        var loginResponse = await client.GetAsync("/account/login");
        var loginHtml = await loginResponse.Content.ReadAsStringAsync();
        var antiforgeryToken = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;

        return await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["username"] = username,
                ["password"] = password,
            }));
    }

    private string CreateSignedToken(
        string issuer,
        string audience,
        DateTime expires,
        string scope = "trade.api",
        string? signingCertificatePath = null,
        string? subject = null,
        string? principalType = null)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            signingCertificatePath ?? newCertificatePath,
            CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", subject ?? userId.ToString()),
            new("scope", scope),
        };
        if (principalType is not null)
        {
            claims.Add(new(PrincipalTypeClaim, principalType));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
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
            BaseAddress = new Uri(Issuer),
            HandleCookies = true,
        });

    private IdentityDbContext CreateIdentityContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                postgres.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name))
            .Options);

    private async Task<TradeSystemDbContext> CreateBusinessContextAsync()
    {
        var context = new TradeSystemDbContext(
            new DbContextOptionsBuilder<TradeSystemDbContext>()
                .UseNpgsql(
                    businessPostgres.GetConnectionString(),
                    npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                        typeof(TradeSystemDbContext).Assembly.GetName().Name))
                .Options);
        await context.Database.MigrateAsync();
        return context;
    }

    private async Task ApplyBusinessMigrationsAsync()
    {
        await using var context = await CreateBusinessContextAsync();
    }

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

        var secondUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = SecondUsername,
        };
        var secondUserResult = await userManager.CreateAsync(secondUser, SecondPassword);
        secondUserResult.Succeeded.Should().BeTrue(
            string.Join(", ", secondUserResult.Errors.Select(error => error.Description)));
        secondUserId = secondUser.Id;

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

    private static ExchangeAccount CreateBusinessAccount(Guid ownerId) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.FromGuid(ownerId),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private string CreateSigningCertificate(
        string name,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Identity Integration Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            notBefore,
            notAfter);
        var path = Path.Combine(Path.GetTempPath(), $"identity-c05a-{name}-{Guid.NewGuid():N}.pfx");
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
        string oldSigningCertificatePath,
        string newSigningCertificatePath,
        string signingCertificatePassword)
        : WebApplicationFactory<Intelligence.TradeSystem.Identity.IdentityApplicationMarker>
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

    private sealed class ApiWebApplicationFactory(
        IdentityWebApplicationFactory identityFactory,
        string businessConnectionString)
        : WebApplicationFactory<Intelligence.TradeSystem.Api.Program>
    {
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

        private readonly RecordingHandler recordingHandler = new(identityFactory.Server.CreateHandler());

        public Uri[] RequestedBackchannelUris => recordingHandler.RequestedUris;
    }

    private sealed class RecordingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private readonly List<Uri> requestedUris = [];

        public Uri[] RequestedUris
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
