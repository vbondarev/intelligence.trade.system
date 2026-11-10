using System.Net.WebSockets;
using System.Text;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class BrowserUpdatesHubTests
{
    private static readonly Uri HubUri = new("ws://localhost/bff/hubs/v1/updates");

    [Fact]
    public async Task Authenticated_browser_with_same_origin_connects()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Origin = "http://localhost";
            request.Headers.Cookie = $"{BffAuthenticationExtensions.SessionCookieName}={browser.SessionCookie}";
        };
        using var socket = await client.ConnectAsync(HubUri, CancellationToken.None);
        socket.State.Should().Be(WebSocketState.Open);
    }

    [Theory]
    [InlineData(false, "http://localhost", "user-subject")]
    [InlineData(true, "http://other", "user-subject")]
    [InlineData(true, "http://localhost", null)]
    public async Task Invalid_session_or_origin_cannot_connect(bool signIn, string origin, string? subject)
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        if (signIn) await browser.SignInAsync(subject: subject);
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Origin = origin;
            if (browser.SessionCookie is not null)
                request.Headers.Cookie = $"{BffAuthenticationExtensions.SessionCookieName}={browser.SessionCookie}";
        };
        var action = async () => await client.ConnectAsync(HubUri, CancellationToken.None);
        await action.Should().ThrowAsync<Exception>();
    }
    [Fact]
    public async Task Negotiate_post_still_requires_antiforgery_token()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        using var response = await browser.Client.PostAsync("/bff/hubs/v1/updates/negotiate?negotiateVersion=1", null);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task Failed_upstream_start_closes_browser_but_preserves_ticket()
    {
        using var factory = new BffApplicationFactory(new Dictionary<string, string?>
        {
            ["Bff:Api:BaseAddress"] = "http://127.0.0.1:1",
        });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Origin = "http://localhost";
            request.Headers.Cookie = $"{BffAuthenticationExtensions.SessionCookieName}={browser.SessionCookie}";
        };
        using var socket = await client.ConnectAsync(HubUri, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[1024];
        try
        {
            while ((await socket.ReceiveAsync(buffer, timeout.Token)).MessageType != WebSocketMessageType.Close) { }
        }
        catch (WebSocketException) { }
        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }
}
