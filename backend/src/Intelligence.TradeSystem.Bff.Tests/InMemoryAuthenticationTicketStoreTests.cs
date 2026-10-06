using System.Globalization;
using System.Security.Claims;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class InMemoryAuthenticationTicketStoreTests : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly MutableTimeProvider time = new(DateTimeOffset.UtcNow);
    private readonly InMemoryAuthenticationTicketStore store;

    public InMemoryAuthenticationTicketStoreTests()
    {
        store = new InMemoryAuthenticationTicketStore(cache, new BffSessionOptions(), time);
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
    public async Task Renew_after_remove_does_not_recreate_session()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));
        var stale = await store.RetrieveAsync(key);
        await store.RemoveAsync(key);

        await store.RenewAsync(key, stale!);

        (await store.RetrieveAsync(key)).Should().BeNull();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Renew_of_unknown_key_does_not_create_session()
    {
        await store.RenewAsync("unknown-key", CreateTicket("access-1"));

        (await store.RetrieveAsync("unknown-key")).Should().BeNull();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Renew_of_expired_session_does_not_recreate_it()
    {
        var ticket = CreateTicket("access-1");
        ticket.Properties.ExpiresUtc = time.GetUtcNow().AddMinutes(5);
        var key = await store.StoreAsync(ticket);
        var stale = await store.RetrieveAsync(key);
        time.Advance(TimeSpan.FromMinutes(5));

        stale!.Properties.ExpiresUtc = time.GetUtcNow().AddMinutes(30);
        await store.RenewAsync(key, stale);

        (await store.RetrieveAsync(key)).Should().BeNull();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Stale_renew_keeps_active_logout_intent()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));
        var stale = await store.RetrieveAsync(key);
        var withIntent = await store.RetrieveAsync(key);
        var intent = FormatInstant(time.GetUtcNow().AddMinutes(2));
        withIntent!.Properties.Items[InMemoryAuthenticationTicketStore.LogoutIntentItem] = intent;
        await store.RenewAsync(key, withIntent);

        await store.RenewAsync(key, stale!);

        (await store.RetrieveAsync(key))!.Properties.Items[InMemoryAuthenticationTicketStore.LogoutIntentItem]
            .Should().Be(intent);
    }

    [Fact]
    public async Task Stale_renew_with_older_logout_intent_keeps_newer_intent()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));
        var stale = await store.RetrieveAsync(key);
        stale!.Properties.Items[InMemoryAuthenticationTicketStore.LogoutIntentItem] =
            FormatInstant(time.GetUtcNow().AddMinutes(1));
        var newer = await store.RetrieveAsync(key);
        var newerIntent = FormatInstant(time.GetUtcNow().AddMinutes(2));
        newer!.Properties.Items[InMemoryAuthenticationTicketStore.LogoutIntentItem] = newerIntent;
        await store.RenewAsync(key, newer);

        await store.RenewAsync(key, stale);

        (await store.RetrieveAsync(key))!.Properties.Items[InMemoryAuthenticationTicketStore.LogoutIntentItem]
            .Should().Be(newerIntent);
    }

    [Fact]
    public async Task Expired_logout_intent_is_not_carried_over_by_renew()
    {
        var key = await store.StoreAsync(CreateTicket("access-1"));
        var withIntent = await store.RetrieveAsync(key);
        withIntent!.Properties.Items[InMemoryAuthenticationTicketStore.LogoutIntentItem] =
            FormatInstant(time.GetUtcNow().AddMinutes(2));
        await store.RenewAsync(key, withIntent);
        time.Advance(TimeSpan.FromMinutes(3));

        await store.RenewAsync(key, CreateTicket("access-1"));

        (await store.RetrieveAsync(key))!.Properties.Items
            .Should().NotContainKey(InMemoryAuthenticationTicketStore.LogoutIntentItem);
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

    private static string FormatInstant(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);

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
            BffAuthenticationExtensions.SessionScheme);
    }
}
