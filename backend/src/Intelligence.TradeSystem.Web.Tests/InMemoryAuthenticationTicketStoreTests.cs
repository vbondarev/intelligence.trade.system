using System.Security.Claims;
using Intelligence.TradeSystem.Web.Authentication;
using Intelligence.TradeSystem.Web.Configuration;
using Intelligence.TradeSystem.Web.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace Intelligence.TradeSystem.Web.Tests;

public sealed class InMemoryAuthenticationTicketStoreTests : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly MutableTimeProvider time = new(DateTimeOffset.UtcNow);
    private readonly InMemoryAuthenticationTicketStore store;

    public InMemoryAuthenticationTicketStoreTests()
    {
        store = new InMemoryAuthenticationTicketStore(cache, new WebSessionOptions(), time);
    }

    [Fact]
    public async Task Store_returns_unpredictable_opaque_key_and_keeps_ticket_server_side()
    {
        var ticket = CreateTicket("access-1");

        var first = await store.StoreAsync(ticket);
        var second = await store.StoreAsync(ticket);

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        first.Should().NotContain("access-1").And.NotContain("user-subject");

        var retrieved = await store.RetrieveAsync(first);
        retrieved!.Properties.GetTokenValue("access_token").Should().Be("access-1");
        retrieved.Properties.Items[InMemoryAuthenticationTicketStore.SessionKeyItem].Should().Be(first);
        retrieved.Principal.FindFirst("sub")!.Value.Should().Be("user-subject");
    }

    [Fact]
    public async Task Retrieve_returns_isolated_copies()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));

        var first = await store.RetrieveAsync(key);
        first!.Properties.UpdateTokenValue("access_token", "mutated");

        var second = await store.RetrieveAsync(key);
        second!.Properties.GetTokenValue("access_token").Should().Be("access-1");
    }

    [Fact]
    public async Task Renew_replaces_ticket_for_same_key()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));
        var renewed = CreateTicket("access-2");
        InMemoryAuthenticationTicketStore.SetTokenGeneration(renewed.Properties, 1);

        await store.RenewAsync(key, renewed);

        (await store.RetrieveAsync(key))!.Properties.GetTokenValue("access_token").Should().Be("access-2");
    }

    [Fact]
    public async Task Renew_with_stale_ticket_does_not_roll_back_newer_token_generation()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));
        var stale = await store.RetrieveAsync(key);
        var refreshed = CreateTicket("access-2", refreshToken: "refresh-2");
        InMemoryAuthenticationTicketStore.SetTokenGeneration(refreshed.Properties, 1);
        await store.RenewAsync(key, refreshed);

        var renewedExpiry = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        stale!.Properties.ExpiresUtc = renewedExpiry;
        await store.RenewAsync(key, stale);

        var current = await store.RetrieveAsync(key);
        current!.Properties.GetTokenValue("access_token").Should().Be("access-2");
        current.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
        InMemoryAuthenticationTicketStore.GetTokenGeneration(current.Properties).Should().Be(1);
        current.Properties.ExpiresUtc.Should().Be(renewedExpiry);
    }

    [Fact]
    public async Task Remove_deletes_ticket()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));

        await store.RemoveAsync(key);

        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task Expired_ticket_is_not_returned_and_is_evicted()
    {
        var ticket = CreateTicket("access-1");
        ticket.Properties.ExpiresUtc = time.GetUtcNow().AddMinutes(5);
        var key = await store.StoreAsync(ticket);

        time.Advance(TimeSpan.FromMinutes(5));

        (await store.RetrieveAsync(key)).Should().BeNull();
        cache.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-key")]
    public async Task Unknown_or_empty_key_returns_null(string? key)
    {
        (await store.RetrieveAsync(key!)).Should().BeNull();
        await store.RemoveAsync(key!);
    }

    [Fact]
    public void Token_generation_defaults_to_zero_for_missing_or_invalid_value()
    {
        var properties = new AuthenticationProperties();
        InMemoryAuthenticationTicketStore.GetTokenGeneration(properties).Should().Be(0);

        properties.Items[InMemoryAuthenticationTicketStore.TokenGenerationItem] = "-1";
        InMemoryAuthenticationTicketStore.GetTokenGeneration(properties).Should().Be(0);

        InMemoryAuthenticationTicketStore.SetTokenGeneration(properties, 7);
        InMemoryAuthenticationTicketStore.GetTokenGeneration(properties).Should().Be(7);
    }

    public void Dispose() => cache.Dispose();

    private static AuthenticationTicket CreateTicket(string accessToken, string refreshToken = "refresh-1")
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = accessToken },
            new AuthenticationToken { Name = "refresh_token", Value = refreshToken },
        ]);

        return new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-subject")], "test")),
            properties,
            WebAuthenticationExtensions.SessionScheme);
    }
}
