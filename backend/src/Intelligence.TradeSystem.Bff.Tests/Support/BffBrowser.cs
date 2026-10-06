using System.Globalization;
using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Bff.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Bff.Tests.Support;

/// <summary>
/// Browser-клиент BFF с собственным cookie jar и без автоматического следования redirects.
/// </summary>
internal sealed class BffBrowser : IDisposable
{
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    private readonly BffApplicationFactory factory;

    public BffBrowser(BffApplicationFactory factory)
    {
        this.factory = factory;
        Client = factory.CreateDefaultClient(
            BffApplicationFactory.BaseAddress,
            new CookieContainerHandler(Cookies));
    }

    public CookieContainer Cookies { get; } = new();

    public HttpClient Client { get; }

    public string? SessionCookie =>
        Cookies.GetCookies(BffApplicationFactory.BaseAddress)[BffAuthenticationExtensions.SessionCookieName]?.Value;

    public async Task SignInAsync(
        string? subject = "user-subject",
        string accessToken = "access-1",
        TimeSpan? accessTokenLifetime = null,
        string? refreshToken = "refresh-1",
        string? idToken = "id-token-1")
    {
        var lifetime = accessTokenLifetime ?? TimeSpan.FromHours(1);
        var query = string.Join(
            '&',
            new[]
            {
                ("sub", subject),
                ("access", accessToken),
                ("lifetime", lifetime.TotalSeconds.ToString(CultureInfo.InvariantCulture)),
                ("refresh", refreshToken),
                ("id", idToken),
            }
            .Where(pair => pair.Item2 is not null)
            .Select(pair => pair.Item1 + "=" + Uri.EscapeDataString(pair.Item2!)));

        using var response = await Client.GetAsync(BffApplicationFactory.SignInPath + "?" + query);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SessionCookie.Should().NotBeNullOrEmpty();
    }

    public async Task<JsonElement> GetSessionAsync(HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        using var response = await Client.GetAsync("/bff/auth/session");
        response.StatusCode.Should().Be(expectedStatus);
        var body = await response.Content.ReadAsStringAsync();
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    public async Task<string> GetAntiforgeryTokenAsync()
    {
        using var response = await Client.GetAsync("/bff/auth/antiforgery");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("requestToken").GetString()!;
    }

    public async Task<HttpResponseMessage> PostLogoutAsync(string? antiforgeryToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/auth/logout");
        if (antiforgeryToken is not null)
        {
            request.Headers.Add(BffAuthenticationExtensions.AntiforgeryHeaderName, antiforgeryToken);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Расшифровывает session cookie так же, как это делает cookie handler BFF.
    /// </summary>
    public AuthenticationTicket? UnprotectSessionCookie()
    {
        var cookie = SessionCookie;
        if (cookie is null)
        {
            return null;
        }

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(BffAuthenticationExtensions.SessionScheme);
        return options.TicketDataFormat.Unprotect(cookie);
    }

    public string? GetSessionKey() =>
        UnprotectSessionCookie()?.Principal.FindFirst(SessionIdClaim)?.Value;

    public async Task<AuthenticationTicket?> GetStoredTicketAsync()
    {
        var key = GetSessionKey();
        if (key is null)
        {
            return null;
        }

        return await factory.Services.GetRequiredService<ITicketStore>().RetrieveAsync(key);
    }

    public void Dispose() => Client.Dispose();
}
