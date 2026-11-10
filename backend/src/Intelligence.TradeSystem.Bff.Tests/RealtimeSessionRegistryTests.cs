using Intelligence.TradeSystem.Bff.Realtime;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class RealtimeSessionRegistryTests
{
    [Fact]
    public async Task Logout_revokes_both_tabs_and_prevents_reentry()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey()!;
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        var aborted = 0;
        (await registry.RegisterAsync(key, "one", () => aborted++, store, "user-subject")).Should().BeTrue();
        (await registry.RegisterAsync(key, "two", () => aborted++, store, "user-subject")).Should().BeTrue();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        using var response = await browser.PostLogoutAsync(antiforgery);
        response.IsSuccessStatusCode.Should().BeTrue();
        aborted.Should().Be(2);
        (await registry.RegisterAsync(key, "three", () => aborted++, store, "user-subject")).Should().BeFalse();
        var sent = false;
        (await registry.SendIfActiveAsync(key, "one", store, "user-subject", () => { sent = true; return Task.CompletedTask; })).Should().BeFalse();
        sent.Should().BeFalse();
    }

    [Fact]
    public async Task Removal_revokes_only_its_session()
    {
        using var factory = new BffApplicationFactory();
        using var first = factory.CreateBrowser();
        using var second = factory.CreateBrowser();
        await first.SignInAsync();
        await second.SignInAsync();
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        var aborted = 0;
        await registry.RegisterAsync(first.GetSessionKey()!, "one", () => aborted++, store, "user-subject");
        await registry.RegisterAsync(second.GetSessionKey()!, "two", () => aborted += 10, store, "user-subject");
        await store.RemoveAsync(first.GetSessionKey()!);
        aborted.Should().Be(1);
        (await registry.IsActiveAsync(second.GetSessionKey()!, "two", store, "user-subject")).Should().BeTrue();
    }
    [Fact]
    public async Task Logout_waits_for_inflight_delivery_and_rejects_later_callbacks()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey()!;
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await registry.RegisterAsync(key, "one", () => { }, store, "user-subject");
        var send = registry.SendIfActiveAsync(key, "one", store, "user-subject", async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;
        var removal = store.RemoveAsync(key);
        removal.IsCompleted.Should().BeFalse();
        release.SetResult();
        (await send).Should().BeTrue();
        await removal;
        (await registry.SendIfActiveAsync(key, "one", store, "user-subject", () => Task.CompletedTask)).Should().BeFalse();
    }
    [Fact]
    public async Task Slow_delivery_does_not_block_another_session()
    {
        using var factory = new BffApplicationFactory();
        using var first = factory.CreateBrowser();
        using var second = factory.CreateBrowser();
        await first.SignInAsync();
        await second.SignInAsync();
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        var firstKey = first.GetSessionKey()!;
        var secondKey = second.GetSessionKey()!;
        await registry.RegisterAsync(firstKey, "one", () => { }, store, "user-subject");
        await registry.RegisterAsync(secondKey, "two", () => { }, store, "user-subject");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = registry.SendIfActiveAsync(firstKey, "one", store, "user-subject", async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;
        var other = registry.SendIfActiveAsync(secondKey, "two", store, "user-subject", () => Task.CompletedTask);
        (await other.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        release.SetResult();
        (await blocked).Should().BeTrue();
    }
    [Fact]
    public async Task Expired_ticket_cannot_receive_or_register_connections()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey()!;
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        (await registry.RegisterAsync(key, "one", () => { }, store, "user-subject")).Should().BeTrue();
        factory.Time.Advance(TimeSpan.FromHours(9));
        (await registry.IsActiveAsync(key, "one", store, "user-subject")).Should().BeFalse();
        (await registry.SendIfActiveAsync(key, "one", store, "user-subject", () => Task.CompletedTask)).Should().BeFalse();
        (await registry.RegisterAsync(key, "two", () => { }, store, "user-subject")).Should().BeFalse();
    }
    [Fact]
    public async Task Expired_intent_allows_new_connection_but_never_restores_revoked_connection()
    {
        using var factory = new BffApplicationFactory();
        using var first = factory.CreateBrowser();
        using var second = factory.CreateBrowser();
        await first.SignInAsync();
        await second.SignInAsync();
        var key = first.GetSessionKey()!;
        var otherKey = second.GetSessionKey()!;
        var registry = factory.Services.GetRequiredService<RealtimeSessionRegistry>();
        var store = factory.Services.GetRequiredService<ITicketStore>();
        var aborted = false;
        (await registry.RegisterAsync(key, "old", () => aborted = true, store, "user-subject")).Should().BeTrue();
        (await registry.RegisterAsync(otherKey, "other", () => { }, store, "user-subject")).Should().BeTrue();
        using var response = await first.PostLogoutAsync(await first.GetAntiforgeryTokenAsync());
        response.IsSuccessStatusCode.Should().BeTrue();
        aborted.Should().BeTrue();
        factory.Time.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromTicks(1));
        (await registry.RegisterAsync(key, "new", () => { }, store, "user-subject")).Should().BeFalse();
        (await registry.IsActiveAsync(otherKey, "other", store, "user-subject")).Should().BeTrue();
        factory.Time.Advance(TimeSpan.FromTicks(1));
        (await registry.RegisterAsync(key, "new", () => { }, store, "user-subject")).Should().BeTrue();
        (await registry.SendIfActiveAsync(key, "old", store, "user-subject", () => Task.CompletedTask)).Should().BeFalse();
    }
}
