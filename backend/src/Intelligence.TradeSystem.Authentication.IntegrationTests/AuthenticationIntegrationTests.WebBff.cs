using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Intelligence.TradeSystem.Identity.Configuration;
using Intelligence.TradeSystem.Identity.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

public sealed partial class AuthenticationIntegrationTests
{
    private const string WebBffScope = "openid offline_access trade.api";
    private const string WebBffState = "web-bff-state";

    private static string WebBffClientId => AuthenticationIntegrationFixture.WebBffClientId;
    private static string WebBffClientSecret => AuthenticationIntegrationFixture.WebBffClientSecret;
    private static string WebBffRedirectUri => AuthenticationIntegrationFixture.WebBffRedirectUri;
    private static string WebBffPostLogoutRedirectUri => AuthenticationIntegrationFixture.WebBffPostLogoutRedirectUri;

    private static string WebBffTestClientId => AuthenticationIntegrationFixture.WebBffTestClientId;
    private static string WebBffTestClientSecret => AuthenticationIntegrationFixture.WebBffTestClientSecret;
    private static string WebBffTestRedirectUri => AuthenticationIntegrationFixture.WebBffTestRedirectUri;
    private static string WebBffTestPostLogoutRedirectUri => AuthenticationIntegrationFixture.WebBffTestPostLogoutRedirectUri;

    [Fact]
    public async Task Web_bff_client_is_registered_as_confidential_pkce_client_with_refresh_and_end_session()
    {
        using var scope = identityFactory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var application = await manager.FindByClientIdAsync(WebBffClientId);

        application.Should().NotBeNull();
        (await manager.GetClientTypeAsync(application!)).Should().Be(ClientTypes.Confidential);
        (await manager.GetPermissionsAsync(application!)).Should().BeEquivalentTo(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.Endpoints.EndSession,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
            Permissions.Prefixes.Scope + "trade.api",
        ]);
        (await manager.GetRequirementsAsync(application!)).Should().Equal(
            Requirements.Features.ProofKeyForCodeExchange);
        (await manager.GetRedirectUrisAsync(application!)).Should().Equal(WebBffRedirectUri);
        (await manager.GetPostLogoutRedirectUrisAsync(application!)).Should().Equal(WebBffPostLogoutRedirectUri);
        (await manager.ValidateClientSecretAsync(application!, WebBffClientSecret)).Should().BeTrue();
        WebBffClientId.Should().Be("trade-web-bff");
    }

    [Fact]
    public async Task Protocol_test_client_is_a_separate_registration_from_the_production_web_bff_client()
    {
        using var scope = identityFactory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var production = await manager.FindByClientIdAsync(WebBffClientId);
        var test = await manager.FindByClientIdAsync(WebBffTestClientId);
        var publicClient = await manager.FindByClientIdAsync(ClientId);

        production.Should().NotBeNull();
        test.Should().NotBeNull();
        publicClient.Should().NotBeNull();
        WebBffTestClientId.Should().Be("trade-web-bff-test").And.NotBe(WebBffClientId);
        (await manager.GetIdAsync(test!)).Should().NotBe(await manager.GetIdAsync(production!));
        (await manager.GetClientTypeAsync(test!)).Should().Be(ClientTypes.Confidential);
        (await manager.GetPermissionsAsync(test!)).Should().BeEquivalentTo(await manager.GetPermissionsAsync(production!));
        (await manager.GetRequirementsAsync(test!)).Should().Equal(Requirements.Features.ProofKeyForCodeExchange);
        (await manager.GetRedirectUrisAsync(test!)).Should().Equal(WebBffTestRedirectUri);
        (await manager.GetPostLogoutRedirectUrisAsync(test!)).Should().Equal(WebBffTestPostLogoutRedirectUri);
        (await manager.ValidateClientSecretAsync(test!, WebBffTestClientSecret)).Should().BeTrue();
        (await manager.ValidateClientSecretAsync(test!, WebBffClientSecret)).Should().BeFalse();
    }

    [Fact]
    public async Task Web_bff_client_seeder_synchronizes_only_its_own_client_and_keeps_the_secret()
    {
        using var scope = identityFactory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var publicClient = await manager.FindByClientIdAsync(ClientId);
        var publicPermissionsBefore = await manager.GetPermissionsAsync(publicClient!);
        var testClient = await manager.FindByClientIdAsync(WebBffTestClientId);
        var testPermissionsBefore = await manager.GetPermissionsAsync(testClient!);
        var webClient = await manager.FindByClientIdAsync(WebBffClientId);
        var drifted = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(drifted, webClient!);
        drifted.RedirectUris.Clear();
        drifted.RedirectUris.Add(new Uri("http://drifted.test/signin-oidc"));
        drifted.Permissions.Remove(Permissions.GrantTypes.RefreshToken);
        await manager.UpdateAsync(webClient!, drifted);

        var seeder = new WebBffClientSeeder(
            identityFactory.Services.GetRequiredService<IServiceScopeFactory>(),
            new WebBffClientOptions
            {
                Enabled = true,
                ClientId = WebBffClientId,
                ClientSecret = WebBffClientSecret,
                RedirectUris = [WebBffRedirectUri],
                PostLogoutRedirectUris = [WebBffPostLogoutRedirectUri],
            },
            NullLogger<WebBffClientSeeder>.Instance);
        await seeder.StartAsync(CancellationToken.None);

        using var verificationScope = identityFactory.Services.CreateScope();
        var verificationManager = verificationScope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var synchronized = await verificationManager.FindByClientIdAsync(WebBffClientId);
        (await verificationManager.GetRedirectUrisAsync(synchronized!)).Should().Equal(WebBffRedirectUri);
        (await verificationManager.GetPermissionsAsync(synchronized!))
            .Should().Contain(Permissions.GrantTypes.RefreshToken);
        (await verificationManager.ValidateClientSecretAsync(synchronized!, WebBffClientSecret)).Should().BeTrue();
        var publicClientAfter = await verificationManager.FindByClientIdAsync(ClientId);
        (await verificationManager.GetPermissionsAsync(publicClientAfter!))
            .Should().BeEquivalentTo(publicPermissionsBefore);
        var testClientAfter = await verificationManager.FindByClientIdAsync(WebBffTestClientId);
        (await verificationManager.GetPermissionsAsync(testClientAfter!)).Should().BeEquivalentTo(testPermissionsBefore);
        (await verificationManager.GetRedirectUrisAsync(testClientAfter!)).Should().Equal(WebBffTestRedirectUri);
        (await verificationManager.GetPostLogoutRedirectUrisAsync(testClientAfter!))
            .Should().Equal(WebBffTestPostLogoutRedirectUri);
        (await verificationManager.ValidateClientSecretAsync(testClientAfter!, WebBffTestClientSecret)).Should().BeTrue();
    }

    [Fact]
    public async Task Web_bff_client_with_secret_and_pkce_receives_code_tokens_and_refresh_token()
    {
        using var client = CreateIdentityClient();
        var authorization = await AuthorizeWebBffAsync(client);

        using var tokenResponse = await RedeemWebBffCodeAsync(
            authorization.Code,
            authorization.CodeVerifier,
            WebBffTestClientSecret);

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("id_token").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("refresh_token").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("expires_in").GetInt32().Should().BePositive();
    }

    [Fact]
    public async Task Web_bff_token_request_without_client_secret_is_rejected()
    {
        using var client = CreateIdentityClient();
        var authorization = await AuthorizeWebBffAsync(client);

        using var tokenResponse = await RedeemWebBffCodeAsync(
            authorization.Code,
            authorization.CodeVerifier,
            clientSecret: null);

        await AssertTokenErrorAsync(tokenResponse, Errors.InvalidClient);
    }

    [Fact]
    public async Task Web_bff_token_request_with_wrong_client_secret_is_rejected()
    {
        using var client = CreateIdentityClient();
        var authorization = await AuthorizeWebBffAsync(client);

        using var tokenResponse = await RedeemWebBffCodeAsync(
            authorization.Code,
            authorization.CodeVerifier,
            "wrong-" + WebBffTestClientSecret);

        await AssertTokenErrorAsync(tokenResponse, Errors.InvalidClient);
    }

    [Fact]
    public async Task Web_bff_confidential_client_still_requires_pkce()
    {
        using var client = CreateIdentityClient();
        await LoginAsync(client, Username, Password);

        using var response = await client.GetAsync(BuildWebBffAuthorizeUri(codeChallenge: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Web_bff_refresh_grant_issues_new_access_token_accepted_by_api_for_the_same_user()
    {
        using var client = CreateIdentityClient();
        var authorization = await AuthorizeWebBffAsync(client);
        using var tokenResponse = await RedeemWebBffCodeAsync(
            authorization.Code,
            authorization.CodeVerifier,
            WebBffTestClientSecret);
        using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var originalAccessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
        var refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;

        using var refreshResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = GrantTypes.RefreshToken,
                ["refresh_token"] = refreshToken,
                ["client_id"] = WebBffTestClientId,
                ["client_secret"] = WebBffTestClientSecret,
            }));

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var refreshed = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        var refreshedAccessToken = refreshed.RootElement.GetProperty("access_token").GetString()!;
        refreshedAccessToken.Should().NotBe(originalAccessToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshedAccessToken);
        jwt.Subject.Should().Be(userId.ToString());
        jwt.Claims.Should().Contain(claim => claim.Type == PrincipalTypeClaim && claim.Value == UserPrincipalType);

        using var apiClient = apiFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAccessToken);
        using var apiResponse = await apiClient.SendAsync(request);
        apiResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var currentUser = JsonDocument.Parse(await apiResponse.Content.ReadAsStringAsync());
        currentUser.RootElement.GetProperty(nameof(userId)).GetGuid().Should().Be(userId);
        currentUser.RootElement.GetProperty("subject").GetString().Should().Be(userId.ToString());
    }

    [Fact]
    public async Task Discovery_publishes_the_end_session_endpoint()
    {
        using var client = CreateIdentityClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var discovery = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        discovery.RootElement.GetProperty("end_session_endpoint").GetString()
            .Should().Be(Issuer + "connect/endsession");
    }

    [Fact]
    public async Task End_session_with_registered_post_logout_redirect_ends_identity_session()
    {
        using var client = CreateIdentityClient();
        var idToken = await IssueWebBffIdTokenAsync(client);

        using var response = await client.GetAsync(BuildEndSessionUri(idToken, WebBffTestPostLogoutRedirectUri));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestPostLogoutRedirectUri);
        QueryHelpers.ParseQuery(location.Query)["state"].ToString().Should().Be(WebBffState);
        response.Headers.GetValues("Set-Cookie")
            .Should().Contain(cookie => cookie.StartsWith("TradeSystem.Identity=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task End_session_rejects_an_unregistered_post_logout_redirect()
    {
        using var client = CreateIdentityClient();
        var idToken = await IssueWebBffIdTokenAsync(client);

        using var response = await client.GetAsync(BuildEndSessionUri(idToken, "http://evil.test/logged-out"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();

        using var authorize = await client.GetAsync(BuildWebBffAuthorizeUri(CreateCodeChallenge(CreateCodeVerifier())));
        authorize.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestRedirectUri);
    }

    [Fact]
    public async Task End_session_without_parameters_keeps_identity_session()
    {
        using var client = CreateIdentityClient();
        await AuthorizeWebBffAsync(client);

        using var response = await client.GetAsync("/connect/endsession");

        await AssertEndSessionRejectedAsync(response);
        await AssertIdentitySessionIsReusedAsync(client);
    }

    [Fact]
    public async Task End_session_with_registered_post_logout_redirect_but_without_id_token_hint_keeps_identity_session()
    {
        using var client = CreateIdentityClient();
        await AuthorizeWebBffAsync(client);

        using var response = await client.GetAsync(
            QueryHelpers.AddQueryString("/connect/endsession", new Dictionary<string, string?>
            {
                ["post_logout_redirect_uri"] = WebBffTestPostLogoutRedirectUri,
                ["state"] = WebBffState,
            }));

        await AssertEndSessionRejectedAsync(response);
        await AssertIdentitySessionIsReusedAsync(client);
    }

    [Fact]
    public async Task End_session_with_id_token_hint_but_without_post_logout_redirect_keeps_identity_session()
    {
        using var client = CreateIdentityClient();
        var idToken = await IssueWebBffIdTokenAsync(client);

        using var response = await client.GetAsync(
            QueryHelpers.AddQueryString("/connect/endsession", "id_token_hint", idToken));

        await AssertEndSessionRejectedAsync(response);
        await AssertIdentitySessionIsReusedAsync(client);
    }

    [Fact]
    public async Task End_session_with_forged_id_token_hint_keeps_identity_session()
    {
        using var client = CreateIdentityClient();
        var idToken = await IssueWebBffIdTokenAsync(client);
        var forged = idToken[..^4] + (idToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        using var response = await client.GetAsync(BuildEndSessionUri(forged, WebBffTestPostLogoutRedirectUri));

        await AssertEndSessionRejectedAsync(response);
        await AssertIdentitySessionIsReusedAsync(client);
    }

    [Fact]
    public async Task End_session_with_id_token_hint_of_another_user_keeps_current_identity_session()
    {
        using var attackerClient = CreateIdentityClient();
        var attackerIdToken = await IssueWebBffIdTokenAsync(attackerClient);
        new JwtSecurityTokenHandler().ReadJwtToken(attackerIdToken).Subject.Should().Be(userId.ToString());
        using var victimClient = CreateIdentityClient();
        await AuthorizeWebBffAsync(victimClient, SecondUsername, SecondPassword);

        using var response = await victimClient.GetAsync(
            BuildEndSessionUri(attackerIdToken, WebBffTestPostLogoutRedirectUri));

        await AssertEndSessionRejectedAsync(response);
        var codeVerifier = CreateCodeVerifier();
        using var authorize = await victimClient.GetAsync(BuildWebBffAuthorizeUri(CreateCodeChallenge(codeVerifier)));
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorize.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestRedirectUri);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location.Query)["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();
        using var tokenResponse = await RedeemWebBffCodeAsync(code, codeVerifier, WebBffTestClientSecret);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        new JwtSecurityTokenHandler()
            .ReadJwtToken(tokens.RootElement.GetProperty("id_token").GetString())
            .Subject.Should().Be(Fixture.SecondUserId.ToString());
    }

    [Fact]
    public async Task End_session_with_valid_id_token_hint_without_identity_session_redirects_to_registered_post_logout_uri()
    {
        using var tokenClient = CreateIdentityClient();
        var idToken = await IssueWebBffIdTokenAsync(tokenClient);
        using var anonymousClient = CreateIdentityClient();

        using var response = await anonymousClient.GetAsync(BuildEndSessionUri(idToken, WebBffTestPostLogoutRedirectUri));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestPostLogoutRedirectUri);
        await AssertIdentitySessionIsReusedAsync(tokenClient);
    }

    [Fact]
    public async Task Authorize_after_end_session_requires_login_again()
    {
        using var client = CreateIdentityClient();
        var idToken = await IssueWebBffIdTokenAsync(client);
        using (await client.GetAsync(BuildEndSessionUri(idToken, WebBffTestPostLogoutRedirectUri)))
        {
        }

        using var authorize = await client.GetAsync(BuildWebBffAuthorizeUri(CreateCodeChallenge(CreateCodeVerifier())));

        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorize.Headers.Location!.AbsolutePath.Should().Be("/account/login");
    }

    [Fact]
    public async Task Authorize_without_prompt_reuses_existing_identity_session()
    {
        using var client = CreateIdentityClient();
        await AuthorizeWebBffAsync(client);

        using var response = await client.GetAsync(BuildWebBffAuthorizeUri(CreateCodeChallenge(CreateCodeVerifier())));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestRedirectUri);
        QueryHelpers.ParseQuery(response.Headers.Location.Query)["code"].ToString()
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Prompt_login_forces_reauthentication_despite_existing_identity_session()
    {
        using var client = CreateIdentityClient();
        await AuthorizeWebBffAsync(client);

        using var response = await client.GetAsync(
            BuildWebBffAuthorizeUri(CreateCodeChallenge(CreateCodeVerifier()), PromptValues.Login));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/account/login");
    }

    [Fact]
    public async Task Prompt_login_cannot_be_bypassed_by_repeating_the_request_without_login()
    {
        using var client = CreateIdentityClient();
        await AuthorizeWebBffAsync(client);
        var promptUri = BuildWebBffAuthorizeUri(CreateCodeChallenge(CreateCodeVerifier()), PromptValues.Login);
        using (await client.GetAsync(promptUri))
        {
        }

        using var repeated = await client.GetAsync(promptUri);

        repeated.StatusCode.Should().Be(HttpStatusCode.Redirect);
        repeated.Headers.Location!.AbsolutePath.Should().Be("/account/login");
        QueryHelpers.ParseQuery(repeated.Headers.Location.Query).Should().NotContainKey("code");
    }

    [Fact]
    public async Task Prompt_login_completes_after_successful_reauthentication_without_loop()
    {
        using var client = CreateIdentityClient();
        await AuthorizeWebBffAsync(client);
        var codeVerifier = CreateCodeVerifier();
        var promptUri = BuildWebBffAuthorizeUri(CreateCodeChallenge(codeVerifier), PromptValues.Login);

        using var challenge = await client.GetAsync(promptUri);
        var completed = await CompleteLoginAsync(client, challenge.Headers.Location!, Username, Password);

        completed.StatusCode.Should().Be(HttpStatusCode.Redirect);
        completed.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestRedirectUri);
        var code = QueryHelpers.ParseQuery(completed.Headers.Location.Query)["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();
        using var tokenResponse = await RedeemWebBffCodeAsync(code, codeVerifier, WebBffTestClientSecret);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Development_user_seeder_creates_user_once_and_rejects_a_different_password()
    {
        var username = "dev-user-" + Guid.NewGuid().ToString("N");
        const string password = "Development-password-123";
        var scopeFactory = identityFactory.Services.GetRequiredService<IServiceScopeFactory>();

        await CreateDevelopmentUserSeeder(scopeFactory, username, password).StartAsync(CancellationToken.None);
        await CreateDevelopmentUserSeeder(scopeFactory, username, password).StartAsync(CancellationToken.None);
        var mismatch = () => CreateDevelopmentUserSeeder(scopeFactory, username, "Other-password-456")
            .StartAsync(CancellationToken.None);

        var exception = (await mismatch.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().NotContain("Other-password-456").And.NotContain(password);
        using var scope = identityFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(username);
        user.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(user!, password)).Should().BeTrue();
    }

    private static DevelopmentUserSeeder CreateDevelopmentUserSeeder(
        IServiceScopeFactory scopeFactory,
        string username,
        string password) =>
        new(
            scopeFactory,
            new DevelopmentUserOptions { Enabled = true, Username = username, Password = password },
            NullLogger<DevelopmentUserSeeder>.Instance);

    private static async Task AssertEndSessionRejectedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Should().Contain(Errors.InvalidRequest);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            cookies.Should().NotContain(cookie => cookie.StartsWith("TradeSystem.Identity=", StringComparison.Ordinal));
        }
    }

    private static async Task AssertIdentitySessionIsReusedAsync(HttpClient client)
    {
        using var authorize = await client.GetAsync(BuildWebBffAuthorizeUri(CreateCodeChallenge(CreateCodeVerifier())));

        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorize.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestRedirectUri);
        QueryHelpers.ParseQuery(authorize.Headers.Location.Query)["code"].ToString()
            .Should().NotBeNullOrWhiteSpace();
    }

    private async Task<string> IssueWebBffIdTokenAsync(HttpClient client)
    {
        var authorization = await AuthorizeWebBffAsync(client);
        using var tokenResponse = await RedeemWebBffCodeAsync(
            authorization.Code,
            authorization.CodeVerifier,
            WebBffTestClientSecret);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id_token").GetString()!;
    }

    private Task<(string Code, string CodeVerifier)> AuthorizeWebBffAsync(HttpClient client) =>
        AuthorizeWebBffAsync(client, Username, Password);

    private static async Task<(string Code, string CodeVerifier)> AuthorizeWebBffAsync(
        HttpClient client,
        string username,
        string password)
    {
        var codeVerifier = CreateCodeVerifier();
        using var challenge = await client.GetAsync(BuildWebBffAuthorizeUri(CreateCodeChallenge(codeVerifier)));
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var completed = await CompleteLoginAsync(client, challenge.Headers.Location!, username, password);
        completed.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var callback = completed.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).Should().Be(WebBffTestRedirectUri);
        var query = QueryHelpers.ParseQuery(callback.Query);
        query["state"].ToString().Should().Be(WebBffState);
        var code = query["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();

        return (code, codeVerifier);
    }

    private static async Task<HttpResponseMessage> CompleteLoginAsync(
        HttpClient client,
        Uri loginLocation,
        string username,
        string password)
    {
        loginLocation.AbsolutePath.Should().Be("/account/login");
        var loginUri = loginLocation.IsAbsoluteUri ? loginLocation.PathAndQuery : loginLocation.OriginalString;
        using var loginPage = await client.GetAsync(loginUri);
        var antiforgeryToken = Regex.Match(
            await loginPage.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var returnUrl = QueryHelpers.ParseQuery(new Uri(new Uri(Issuer), loginUri).Query)["returnUrl"].ToString();

        using var loginPost = await client.PostAsync(
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
        return await client.GetAsync(
            authorizationLocation.IsAbsoluteUri
                ? authorizationLocation.PathAndQuery
                : authorizationLocation.OriginalString);
    }

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await SubmitLoginAsync(client, username, password);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    private async Task<HttpResponseMessage> RedeemWebBffCodeAsync(
        string code,
        string codeVerifier,
        string? clientSecret)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = GrantTypes.AuthorizationCode,
            ["client_id"] = WebBffTestClientId,
            ["code"] = code,
            ["redirect_uri"] = WebBffTestRedirectUri,
            ["code_verifier"] = codeVerifier,
        };
        if (clientSecret is not null)
        {
            parameters["client_secret"] = clientSecret;
        }

        using var client = CreateIdentityClient();
        return await client.PostAsync("/connect/token", new FormUrlEncodedContent(parameters));
    }

    private static async Task AssertTokenErrorAsync(HttpResponseMessage response, string expectedError)
    {
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be(expectedError);
        body.RootElement.TryGetProperty("access_token", out _).Should().BeFalse();
    }

    private static string BuildWebBffAuthorizeUri(string? codeChallenge, string? prompt = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = WebBffTestClientId,
            ["redirect_uri"] = WebBffTestRedirectUri,
            ["response_type"] = ResponseTypes.Code,
            ["scope"] = WebBffScope,
            ["state"] = WebBffState,
            ["nonce"] = "web-bff-nonce",
        };
        if (codeChallenge is not null)
        {
            query["code_challenge"] = codeChallenge;
            query["code_challenge_method"] = CodeChallengeMethods.Sha256;
        }

        if (prompt is not null)
        {
            query["prompt"] = prompt;
        }

        return QueryHelpers.AddQueryString("/connect/authorize", query);
    }

    private static string BuildEndSessionUri(string idTokenHint, string postLogoutRedirectUri) =>
        QueryHelpers.AddQueryString("/connect/endsession", new Dictionary<string, string?>
        {
            ["id_token_hint"] = idTokenHint,
            ["post_logout_redirect_uri"] = postLogoutRedirectUri,
            ["state"] = WebBffState,
        });

    private static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string codeVerifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
}
