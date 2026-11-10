using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Intelligence.TradeSystem.Bff.Realtime;

[Authorize(Policy = "BrowserRealtime")]
public sealed class BrowserUpdatesHub : Hub
{
    private const string StateKey = "realtime-state";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var auth = http is null ? null : await http.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        var subject = auth?.Principal?.FindFirst("sub")?.Value;
        if (http is null || auth?.Ticket is null || string.IsNullOrWhiteSpace(subject)
            || !auth.Ticket.Properties.Items.TryGetValue(InMemoryAuthenticationTicketStore.SessionKeyItem, out var key)
            || string.IsNullOrEmpty(key))
        {
            Context.Abort();
            return;
        }

        var services = http.RequestServices;
        var registry = services.GetRequiredService<RealtimeSessionRegistry>();
        var store = services.GetRequiredService<ITicketStore>();
        if (!await registry.RegisterAsync(key, Context.ConnectionId, Context.Abort, store, subject))
        {
            Context.Abort();
            return;
        }

        BrowserRealtimeBridge? bridge = null;
        try
        {
            bridge = new BrowserRealtimeBridge(key, subject, Context.ConnectionId, registry, store,
                services.GetRequiredService<BffTokenService>(), services.GetRequiredService<IHubContext<BrowserUpdatesHub>>(),
                services.GetRequiredService<BffConfiguration>().ApiBaseAddress, Context.Abort, Context.ConnectionAborted);
            Context.Items[StateKey] = bridge;
            await bridge.StartAsync();
            await base.OnConnectedAsync();
        }
        catch
        {
            Context.Items.Remove(StateKey);
            Context.Abort();
            if (bridge is null) await registry.UnregisterAsync(key, Context.ConnectionId);
            else await bridge.DisposeAsync();
            throw;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(StateKey, out var value) && value is BrowserRealtimeBridge state)
            await state.DisposeAsync();
        await base.OnDisconnectedAsync(exception);
    }
}
