using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class BrowserRealtimeBridgeTests
{
    [Fact]
    public async Task Relays_four_events_with_server_side_bearer_and_ready()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(new UpstreamProbe());
        await using var upstream = builder.Build();
        upstream.MapHub<TestUpstreamHub>("/hubs/v1/updates");
        await upstream.StartAsync();
        var address = upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var factory = new BffApplicationFactory(new Dictionary<string, string?> { ["Bff:Api:BaseAddress"] = address });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "secret-server-token");
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Origin = "http://localhost";
            request.Headers.Cookie = $"{BffAuthenticationExtensions.SessionCookieName}={browser.SessionCookie}";
        };
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/bff/hubs/v1/updates"), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var probe = upstream.Services.GetRequiredService<UpstreamProbe>();
        var connected = await probe.Connections.Reader.ReadAsync(timeout.Token);
        connected.Authorization.Should().Be("Bearer secret-server-token");
        connected.Query.Should().BeEmpty();
        var ready = await ReadUntilAsync(socket, "bff.realtime.ready", timeout.Token);
        ready.Should().NotContain("secret-server-token");
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinUser\",\"arguments\":[\"other-user\"]}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var denied = await ReadUntilAsync(socket, "\"invocationId\":\"1\"", timeout.Token);
        denied.Should().Contain("error");
        using var secondBrowser = factory.CreateBrowser();
        await secondBrowser.SignInAsync(subject: "other-subject", accessToken: "other-server-token");
        var secondClient = factory.Server.CreateWebSocketClient();
        secondClient.ConfigureRequest = request =>
        {
            request.Headers.Origin = "http://localhost";
            request.Headers.Cookie = $"{BffAuthenticationExtensions.SessionCookieName}={secondBrowser.SessionCookie}";
        };
        using var secondSocket = await secondClient.ConnectAsync(new Uri("ws://localhost/bff/hubs/v1/updates"), timeout.Token);
        await secondSocket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var second = await probe.Connections.Reader.ReadAsync(timeout.Token);
        second.Authorization.Should().Be("Bearer other-server-token");
        await ReadUntilAsync(secondSocket, "bff.realtime.ready", timeout.Token);
        var hub = upstream.Services.GetRequiredService<IHubContext<TestUpstreamHub>>();
        var eventId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        foreach (var name in new[] { "exchangeAccount.updated", "portfolio.updated", "position.updated", "evaluation.updated" })
        {
            object payload = name.StartsWith("exchange", StringComparison.Ordinal) || name.StartsWith("portfolio", StringComparison.Ordinal)
                ? new { eventId, occurredAt = DateTimeOffset.UtcNow, exchangeAccountId = resourceId }
                : new { eventId, occurredAt = DateTimeOffset.UtcNow, positionId = resourceId };
            await hub.Clients.Client(connected.Id).SendAsync(name, payload, timeout.Token);
            var relayed = await ReadUntilAsync(socket, name, timeout.Token);
            relayed.Should().Contain(eventId.ToString()).And.Contain(resourceId.ToString()).And.NotContain("secret-server-token");
        }
        var otherEventId = Guid.NewGuid();
        await hub.Clients.Client(second.Id).SendAsync("position.updated", new { eventId = otherEventId, occurredAt = DateTimeOffset.UtcNow, positionId = resourceId }, timeout.Token);
        var otherUserDelivery = await ReadUntilAsync(secondSocket, otherEventId.ToString(), timeout.Token);
        otherUserDelivery.Should().NotContain(eventId.ToString()).And.NotContain("secret-server-token");
        connected.Abort();
        await WaitClosedAsync(socket, timeout.Token);
        var antiforgery = await secondBrowser.GetAntiforgeryTokenAsync();
        using var response = await secondBrowser.PostLogoutAsync(antiforgery);
        response.IsSuccessStatusCode.Should().BeTrue();
        await WaitClosedAsync(secondSocket, timeout.Token);
    }

    private static async Task WaitClosedAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while ((await socket.ReceiveAsync(buffer, cancellationToken)).MessageType != WebSocketMessageType.Close) { }
    }

    private static async Task<string> ReadUntilAsync(WebSocket socket, string text, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var received = new StringBuilder();
        while (!received.ToString().Contains(text, StringComparison.Ordinal))
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Соединение закрыто до события.");
            received.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        return received.ToString();
    }

    public sealed class UpstreamProbe
    {
        public Channel<(string Id, string Authorization, string Query, Action Abort)> Connections { get; } = Channel.CreateUnbounded<(string, string, string, Action)>();
    }

    public sealed class TestUpstreamHub(UpstreamProbe probe) : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var request = Context.GetHttpContext()!.Request;
            await probe.Connections.Writer.WriteAsync((Context.ConnectionId, request.Headers.Authorization.ToString(), request.QueryString.Value ?? "", Context.Abort));
            await base.OnConnectedAsync();
        }
    }
}
