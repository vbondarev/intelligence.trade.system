using Intelligence.TradeSystem.Bff.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Bff.Endpoints;

internal static partial class AuthEndpoints
{
    private static async Task<IResult> GetSessionTimingAsync(
        HttpContext context, InMemoryAuthenticationTicketStore store, TimeProvider clock)
    {
        var key = await GetSessionKeyAsync(context);
        var ticket = key is null ? null : await store.RetrieveAsync(key);
        return ticket is not null && !InMemoryAuthenticationTicketStore.HasActiveLogoutIntent(ticket.Properties, clock)
            ? SessionTiming(ticket, clock)
            : Results.Unauthorized();
    }

    private static async Task<IResult> ExtendSessionAsync(
        HttpContext context,
        InMemoryAuthenticationTicketStore store,
        TimeProvider clock,
        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions)
    {
        var key = await GetSessionKeyAsync(context);
        var options = cookieOptions.Get(BffAuthenticationExtensions.SessionScheme);
        var name = BffAuthenticationExtensions.SessionCookieName;
        var value = options.CookieManager.GetRequestCookie(context, name);
        if (key is null || string.IsNullOrEmpty(value) || store.Extend(key) is not { } ticket)
        {
            return Results.Unauthorized();
        }

        // Cookie содержит только защищённую ссылку на тот же ticket. Переиздаём её штатным
        // CookieManager без sign-in, новых claims или возможности создать session.
        var cookie = options.Cookie.Build(context);
        cookie.Expires = ticket.Properties.IsPersistent ? ticket.Properties.ExpiresUtc : null;
        options.CookieManager.AppendResponseCookie(context, name, value, cookie);
        return SessionTiming(ticket, clock);
    }

    private static async Task<string?> GetSessionKeyAsync(HttpContext context)
    {
        var authentication = await context.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        return authentication.Succeeded
            && authentication.Properties!.Items.TryGetValue(InMemoryAuthenticationTicketStore.SessionKeyItem, out var key)
            && !string.IsNullOrEmpty(key)
            ? key
            : null;
    }

    private static IResult SessionTiming(AuthenticationTicket ticket, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return ticket.Properties.ExpiresUtc is { } expires && expires > now
            ? Results.Ok(new { serverTimeUtc = now, expiresAtUtc = expires })
            : Results.Unauthorized();
    }
}
