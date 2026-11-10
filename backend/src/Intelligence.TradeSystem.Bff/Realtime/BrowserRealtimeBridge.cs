using Intelligence.TradeSystem.Bff.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Intelligence.TradeSystem.Bff.Realtime;

internal sealed class BrowserRealtimeBridge : IAsyncDisposable
{
    private readonly string sessionKey;
    private readonly string subject;
    private readonly string connectionId;
    private readonly RealtimeSessionRegistry registry;
    private readonly ITicketStore store;
    private readonly IHubContext<BrowserUpdatesHub> hub;
    private readonly CancellationToken connectionAborted;
    private readonly Action abort;
    private readonly HubConnection upstream;
    private readonly CancellationTokenSource stopping = new();
    private Task? monitor;

    internal BrowserRealtimeBridge(string sessionKey, string subject, string connectionId,
        RealtimeSessionRegistry registry, ITicketStore store, BffTokenService tokenService,
        IHubContext<BrowserUpdatesHub> hub, Uri apiBase, Action abort, CancellationToken connectionAborted)
    {
        this.sessionKey = sessionKey;
        this.subject = subject;
        this.connectionId = connectionId;
        this.registry = registry;
        this.store = store;
        this.hub = hub;
        this.connectionAborted = connectionAborted;
        this.abort = abort;
        upstream = new HubConnectionBuilder().WithUrl(new Uri(apiBase, "/hubs/v1/updates"), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.AccessTokenProvider = async () =>
            {
                var token = await tokenService.GetAccessTokenForSessionAsync(sessionKey, subject, connectionAborted);
                return token.Status == BffAccessTokenStatus.Available
                    ? token.AccessToken : throw new InvalidOperationException("BFF session недоступна для realtime.");
            };
        }).Build();
        upstream.On<AccountUpdateMessage>("exchangeAccount.updated", message => ForwardAsync("exchangeAccount.updated", message));
        upstream.On<AccountUpdateMessage>("portfolio.updated", message => ForwardAsync("portfolio.updated", message));
        upstream.On<PositionUpdateMessage>("position.updated", message => ForwardAsync("position.updated", message));
        upstream.On<PositionUpdateMessage>("evaluation.updated", message => ForwardAsync("evaluation.updated", message));
        upstream.Closed += _ => { abort(); return Task.CompletedTask; };
    }

    internal async Task StartAsync()
    {
        monitor = MonitorAsync();
        await upstream.StartAsync(connectionAborted);
        if (!await registry.SendIfActiveAsync(sessionKey, connectionId, store, subject,
            () => hub.Clients.Client(connectionId).SendCoreAsync("bff.realtime.ready", [], connectionAborted)))
            abort();
    }

    private async Task ForwardAsync<T>(string eventName, T message)
    {
        if (!await registry.SendIfActiveAsync(sessionKey, connectionId, store, subject,
            () => hub.Clients.Client(connectionId).SendCoreAsync(eventName, [message], connectionAborted)))
            abort();
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                if (!await registry.IsActiveAsync(sessionKey, connectionId, store, subject))
                {
                    abort();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await stopping.CancelAsync();
            if (monitor is not null) await monitor;
        }
        finally
        {
            try { await upstream.DisposeAsync(); }
            finally
            {
                try { await registry.UnregisterAsync(sessionKey, connectionId); }
                finally { stopping.Dispose(); }
            }
        }
    }
}
