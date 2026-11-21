using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Realtime;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class SessionTimingTests
{
    private const string Timing = "/bff/auth/session/timing";
    private const string Extend = "/bff/auth/session/extend";

    [Fact]
    public async Task Ten_explicit_extensions_keep_the_same_ticket_and_reissue_the_opaque_cookie()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey();
        var expiry = (await browser.GetStoredTicketAsync())!.Properties.ExpiresUtc;

        for (var i = 0; i < 10; i++)
        {
            factory.Time.Advance(TimeSpan.FromHours(8) - TimeSpan.FromMinutes(2));
            var tokens = await browser.GetAntiforgeryTokenAsync();
            using var read = await browser.Client.GetAsync(Timing + "/");
            read.StatusCode.Should().Be(HttpStatusCode.OK);
            read.Headers.CacheControl!.NoStore.Should().BeTrue();
            read.Headers.Contains("Set-Cookie").Should().BeFalse();
            (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresAtUtc").GetDateTimeOffset()
                .Should().Be(expiry!.Value);

            using var response = await PostAsync(browser, tokens);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            var setCookie = response.Headers.GetValues("Set-Cookie").Single();
            setCookie.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=lax");
            browser.GetSessionKey().Should().Be(key);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            result.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("expiresAtUtc", "serverTimeUtc");
            expiry = DateTimeOffset.FromUnixTimeSeconds(factory.Time.GetUtcNow().AddHours(8).ToUnixTimeSeconds());
            result.GetProperty("expiresAtUtc").GetDateTimeOffset().Should().Be(expiry.Value);
            (await browser.GetStoredTicketAsync())!.Properties.ExpiresUtc.Should().Be(expiry);
        }
    }

    [Fact]
    public async Task Timing_and_an_invalid_csrf_post_leave_expiry_intact()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var expiry = (await browser.GetStoredTicketAsync())!.Properties.ExpiresUtc;
        factory.Time.Advance(TimeSpan.FromHours(7));

        using var get = await browser.Client.GetAsync(Timing);
        using var rejected = await PostAsync(browser, null);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        rejected.Headers.Contains("Set-Cookie").Should().BeFalse();
        (await browser.GetStoredTicketAsync())!.Properties.ExpiresUtc.Should().Be(expiry);

        factory.Time.Advance(TimeSpan.FromHours(1));
        using var expired = await browser.Client.GetAsync(Timing);
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browser.GetStoredTicketAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Logout_intent_blocks_extension_and_supersedes_a_concurrent_refresh()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        var key = browser.GetSessionKey()!;
        var stale = (await browser.GetStoredTicketAsync())!;
        var expiry = stale.Properties.ExpiresUtc;
        using var logout = await browser.PostLogoutAsync(antiforgery);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var store = factory.Services.GetRequiredService<ITicketStore>();
        stale.Properties.ExpiresUtc = expiry!.Value.AddHours(1);
        await store.RenewAsync(key, stale);
        using var late = await PostAsync(browser, antiforgery);
        late.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        late.Headers.Contains("Set-Cookie").Should().BeFalse();
        (await browser.GetStoredTicketAsync())!.Properties.ExpiresUtc.Should().Be(expiry);

        await store.RemoveAsync(key);
        await store.RenewAsync(key, stale);
        using var afterRemoval = await browser.Client.GetAsync(Timing);
        afterRemoval.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task Extension_and_token_refresh_preserve_both_expiry_and_the_latest_tokens()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey()!;
        var refreshSnapshot = (await browser.GetStoredTicketAsync())!;
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        factory.Time.Advance(TimeSpan.FromHours(7));
        using var extended = await PostAsync(browser, antiforgery);
        extended.StatusCode.Should().Be(HttpStatusCode.OK);

        var store = factory.Services.GetRequiredService<ITicketStore>();
        refreshSnapshot.Properties.UpdateTokenValue("access_token", "new-generation");
        InMemoryAuthenticationTicketStore.SetTokenGeneration(refreshSnapshot.Properties, 1);
        await store.RenewAsync(key, refreshSnapshot);
        var current = (await browser.GetStoredTicketAsync())!;
        current.Properties.ExpiresUtc.Should().BeCloseTo(factory.Time.GetUtcNow().AddHours(8), TimeSpan.FromSeconds(1));
        current.Properties.GetTokenValue("access_token").Should().Be("new-generation");
    }

    [Fact]
    public async Task Realtime_delivery_uses_the_extended_deadline_and_stops_at_expiry()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey()!;
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        (await registry.RegisterAsync(key, "tab", () => { }, store, "user-subject")).Should().BeTrue();

        factory.Time.Advance(TimeSpan.FromHours(7));
        using var extension = await PostAsync(browser, antiforgery);
        extension.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Time.Advance(TimeSpan.FromHours(2));
        (await registry.SendIfActiveAsync(key, "tab", store, "user-subject", () => Task.CompletedTask)).Should().BeTrue();

        factory.Time.Advance(TimeSpan.FromHours(6));
        (await registry.SendIfActiveAsync(key, "tab", store, "user-subject", () => Task.CompletedTask)).Should().BeFalse();
        (await registry.RegisterAsync(key, "reconnect", () => { }, store, "user-subject")).Should().BeFalse();
    }

    private static async Task<HttpResponseMessage> PostAsync(BffBrowser browser, string? antiforgery)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Extend);
        if (antiforgery is not null)
        {
            request.Headers.Add(BffAuthenticationExtensions.AntiforgeryHeaderName, antiforgery);
        }
        return await browser.Client.SendAsync(request);
    }
}
