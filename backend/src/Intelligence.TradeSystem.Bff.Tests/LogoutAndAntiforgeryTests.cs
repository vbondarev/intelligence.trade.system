using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class LogoutAndAntiforgeryTests
{
    [Fact]
    public async Task Logout_post_without_antiforgery_header_is_rejected()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        await browser.GetAntiforgeryTokenAsync();

        using var response = await browser.PostLogoutAsync(antiforgeryToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        (await browser.GetStoredTicketAsync())!.Properties.Items
            .Should().NotContainKey("logout_intent_expires_at");
    }

    [Fact]
    public async Task Logout_post_with_foreign_antiforgery_token_is_rejected()
    {
        using var factory = CreateFactoryWithApi();
        using var attacker = factory.CreateBrowser();
        await attacker.SignInAsync(subject: "attacker");
        var attackerToken = await attacker.GetAntiforgeryTokenAsync();
        using var victim = factory.CreateBrowser();
        await victim.SignInAsync(subject: "victim");
        await victim.GetAntiforgeryTokenAsync();

        using var response = await victim.PostLogoutAsync(attackerToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Antiforgery_token_requires_session_and_is_not_cached()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();

        using (var anonymous = await browser.Client.GetAsync("/bff/auth/antiforgery"))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await browser.SignInAsync();
        using var response = await browser.Client.GetAsync("/bff/auth/antiforgery");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var antiforgeryCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(BffAuthenticationExtensions.AntiforgeryCookieName + "=", StringComparison.Ordinal));
        antiforgeryCookie.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=strict");
    }

    [Fact]
    public async Task Logout_post_after_server_side_session_loss_does_not_create_intent()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var token = await browser.GetAntiforgeryTokenAsync();
        await factory.Services.GetTicketStore().RemoveAsync(browser.GetSessionKey()!);

        using var response = await browser.PostLogoutAsync(token);

        // Antiforgery token привязан к пользователю утраченной session и больше не принимается.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirmed_logout_initiates_identity_end_session_and_ends_bff_session()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(idToken: "id-token-for-end-session");
        var token = await browser.GetAntiforgeryTokenAsync();
        var sessionKey = browser.GetSessionKey();

        using (var logout = await browser.PostLogoutAsync(token))
        {
            logout.StatusCode.Should().Be(HttpStatusCode.OK);
            logout.Headers.CacheControl!.NoStore.Should().BeTrue();
            using var document = JsonDocument.Parse(await logout.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("redirectUrl").GetString().Should().Be("/bff/auth/logout/complete");
        }

        (await browser.GetStoredTicketAsync())!.Properties.Items
            .Should().ContainKey("logout_intent_expires_at");

        using var complete = await browser.Client.GetAsync("/bff/auth/logout/complete");

        complete.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = complete.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(BffApplicationFactory.OidcConfiguration.EndSessionEndpoint);
        var query = QueryHelpers.ParseQuery(location.Query);
        query["post_logout_redirect_uri"].ToString().Should().Be("http://localhost/signout-callback-oidc");
        query["id_token_hint"].ToString().Should().Be("id-token-for-end-session");
        query.Should().ContainKey("state");

        browser.SessionCookie.Should().BeNull();
        (await factory.Services.GetTicketStore().RetrieveAsync(sessionKey!)).Should().BeNull();
        var session = await browser.GetSessionAsync();
        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Logout_complete_without_prior_intent_keeps_session()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var complete = await browser.Client.GetAsync("/bff/auth/logout/complete");

        complete.StatusCode.Should().Be(HttpStatusCode.Redirect);
        complete.Headers.Location!.OriginalString.Should().Be("/");
        var session = await browser.GetSessionAsync();
        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Logout_complete_with_expired_intent_keeps_session()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var token = await browser.GetAntiforgeryTokenAsync();
        using (var logout = await browser.PostLogoutAsync(token))
        {
            logout.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        factory.Time.Advance(TimeSpan.FromMinutes(3));
        using var complete = await browser.Client.GetAsync("/bff/auth/logout/complete");

        complete.StatusCode.Should().Be(HttpStatusCode.Redirect);
        complete.Headers.Location!.OriginalString.Should().Be("/");
        var session = await browser.GetSessionAsync();
        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_logout_complete_redirects_home()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();

        using var complete = await browser.Client.GetAsync("/bff/auth/logout/complete");

        complete.StatusCode.Should().Be(HttpStatusCode.Redirect);
        complete.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task Logout_during_in_flight_refresh_is_not_undone_by_refresh_completion()
    {
        using var factory = CreateFactoryWithApi();
        var refresh = BlockTokenEndpoint(factory);
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));
        var token = await browser.GetAntiforgeryTokenAsync();
        var sessionKey = browser.GetSessionKey();

        var inFlight = browser.Client.GetAsync("/bff/auth/session");
        await refresh.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using (var logout = await browser.PostLogoutAsync(token))
        {
            logout.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var complete = await browser.Client.GetAsync("/bff/auth/logout/complete"))
        {
            complete.Headers.Location!.GetLeftPart(UriPartial.Path)
                .Should().Be(BffApplicationFactory.OidcConfiguration.EndSessionEndpoint);
        }

        refresh.Release.SetResult();
        using (var refreshed = await inFlight)
        {
            refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        }

        (await factory.Services.GetTicketStore().RetrieveAsync(sessionKey!)).Should().BeNull();
        var session = await browser.GetSessionAsync();
        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        factory.Api.Count.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_completed_after_logout_post_keeps_logout_intent()
    {
        using var factory = CreateFactoryWithApi();
        var refresh = BlockTokenEndpoint(factory);
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));
        var token = await browser.GetAntiforgeryTokenAsync();
        var sessionKey = browser.GetSessionKey();

        var inFlight = browser.Client.GetAsync("/bff/auth/session");
        await refresh.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var logout = await browser.PostLogoutAsync(token))
        {
            logout.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        refresh.Release.SetResult();
        using (var refreshed = await inFlight)
        {
            refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await browser.GetStoredTicketAsync())!.Properties.Items
            .Should().ContainKey("logout_intent_expires_at");
        using var complete = await browser.Client.GetAsync("/bff/auth/logout/complete");
        complete.Headers.Location!.GetLeftPart(UriPartial.Path)
            .Should().Be(BffApplicationFactory.OidcConfiguration.EndSessionEndpoint);
        (await factory.Services.GetTicketStore().RetrieveAsync(sessionKey!)).Should().BeNull();
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task State_changing_bff_requests_require_antiforgery_token(string method)
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), "/bff/unknown");
        using var response = await browser.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static BffApplicationFactory CreateFactoryWithApi()
    {
        var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(UpstreamResponses.CurrentUser());
        return factory;
    }

    private static BlockedTokenEndpoint BlockTokenEndpoint(BffApplicationFactory factory)
    {
        var blocked = new BlockedTokenEndpoint();
        factory.TokenEndpoint.Responder = async (_, cancellationToken) =>
        {
            blocked.Received.TrySetResult();
            await blocked.Release.Task.WaitAsync(cancellationToken);
            return UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2");
        };
        return blocked;
    }

    private sealed class BlockedTokenEndpoint
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
