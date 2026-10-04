using System.Net;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class LoginAndHostingTests
{
    [Fact]
    public async Task Login_starts_authorization_code_flow_with_pkce_and_required_scopes()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync("/bff/auth/login?returnUrl=/app");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(BffApplicationFactory.OidcConfiguration.AuthorizationEndpoint);
        var query = QueryHelpers.ParseQuery(location.Query);
        query["client_id"].ToString().Should().Be(BffApplicationFactory.ClientId);
        query["response_type"].ToString().Should().Be("code");
        query["redirect_uri"].ToString().Should().Be("http://localhost/signin-oidc");
        query["code_challenge_method"].ToString().Should().Be("S256");
        query["code_challenge"].ToString().Should().NotBeNullOrEmpty();
        query["scope"].ToString().Split(' ').Should().BeEquivalentTo("openid", "offline_access", "trade.api");
        query.Should().NotContainKey("prompt");
        query.Should().NotContainKey("client_secret");
        location.Query.Should().NotContain(BffApplicationFactory.ClientSecret);
    }

    [Theory]
    [InlineData("https://evil.example/app")]
    [InlineData("//evil.example/app")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/\\evil.example")]
    public async Task Login_rejects_non_local_return_url(string returnUrl)
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync(
            "/bff/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Login_with_existing_session_redirects_to_local_return_url()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync("/bff/auth/login?returnUrl=/app/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/app/settings");
    }

    [Fact]
    public async Task Session_cookie_is_http_only_lax_and_contains_only_opaque_session_reference()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync(
            BffApplicationFactory.SignInPath + "?sub=user-subject&access=opaque-access&lifetime=3600&refresh=opaque-refresh&id=opaque-id");

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(BffAuthenticationExtensions.SessionCookieName + "=", StringComparison.Ordinal));
        setCookie.Should().ContainEquivalentOf("httponly")
            .And.ContainEquivalentOf("samesite=lax")
            .And.ContainEquivalentOf("path=/");

        var cookieTicket = browser.UnprotectSessionCookie();
        cookieTicket.Should().NotBeNull();
        cookieTicket!.Principal.Claims.Select(claim => claim.Type)
            .Should().Equal("Microsoft.AspNetCore.Authentication.Cookies-SessionId");
        cookieTicket.Properties.Items.Should().NotContainKey(".Token.access_token");
        cookieTicket.Properties.Items.Keys.Should().NotContain(key => key.StartsWith(".Token.", StringComparison.Ordinal));

        var stored = await browser.GetStoredTicketAsync();
        stored!.Properties.Items.Should().ContainKey(".Token.access_token");
        stored.Principal.FindFirst("sub")!.Value.Should().Be("user-subject");
    }

    [Fact]
    public async Task Unknown_bff_route_returns_not_found_instead_of_spa()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync("/bff/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(BffApplicationFactory.SpaIndexMarker);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/alive")]
    [InlineData("/signin-oidc")]
    [InlineData("/signout-callback-oidc")]
    public async Task Health_and_oidc_callback_paths_are_not_served_by_spa_fallback(string path)
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync(path);

        (await response.Content.ReadAsStringAsync()).Should().NotContain(BffApplicationFactory.SpaIndexMarker);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app")]
    [InlineData("/app/nested/route")]
    public async Task Spa_routes_fall_back_to_index(string path)
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(BffApplicationFactory.SpaIndexMarker);
    }
}
