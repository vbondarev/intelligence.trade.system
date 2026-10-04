using System.Globalization;
using Intelligence.TradeSystem.Bff.Api;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Contracts.Auth;
using Intelligence.TradeSystem.Bff.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Intelligence.TradeSystem.Bff.Endpoints;

/// <summary>
/// Browser-facing endpoints BFF session. Endpoints не возвращают tokens и не выполняют
/// redirect на внешние адреса, кроме OIDC challenge/sign-out к настроенному Identity.
/// </summary>
internal static partial class AuthEndpoints
{
    internal const string LogoutIntentItem = InMemoryAuthenticationTicketStore.LogoutIntentItem;
    internal const string LogoutCompletePath = "/bff/auth/logout/complete";

    internal static readonly TimeSpan LogoutIntentLifetime = TimeSpan.FromMinutes(2);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/auth")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        group.MapGet("/session", GetSessionAsync);
        group.MapGet("/login", LoginAsync);
        group.MapGet("/antiforgery", GetAntiforgeryTokenAsync);
        group.MapPost("/logout", BeginLogoutAsync);
        group.MapGet("/logout/complete", CompleteLogoutAsync);

        endpoints.Map("/bff/{**path}", () => Results.NotFound());

        return endpoints;
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext httpContext,
        BffTokenService tokenService,
        CurrentUserApiClient apiClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(AuthEndpoints));

        var token = await tokenService.GetAccessTokenAsync(httpContext, null, cancellationToken);
        if (token.Status == BffAccessTokenStatus.SessionEnded)
        {
            return Results.Ok(BrowserSessionResponse.Anonymous);
        }

        if (token.Status == BffAccessTokenStatus.TemporarilyUnavailable)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var currentUser = await apiClient.GetCurrentUserAsync(token.AccessToken!, cancellationToken);
        if (currentUser.Status == CurrentUserApiStatus.Unauthorized)
        {
            // API отклонил token до его ожидаемого истечения: один принудительный refresh и один retry.
            var refreshed = await tokenService.GetAccessTokenAsync(httpContext, token.AccessToken, cancellationToken);
            if (refreshed.Status == BffAccessTokenStatus.SessionEnded)
            {
                return Results.Ok(BrowserSessionResponse.Anonymous);
            }

            if (refreshed.Status == BffAccessTokenStatus.TemporarilyUnavailable)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            currentUser = await apiClient.GetCurrentUserAsync(refreshed.AccessToken!, cancellationToken);
            if (currentUser.Status == CurrentUserApiStatus.Unauthorized)
            {
                LogSessionRejectedByApi(logger);
                await httpContext.SignOutAsync(BffAuthenticationExtensions.SessionScheme);
                return Results.Ok(BrowserSessionResponse.Anonymous);
            }
        }

        return currentUser.Status switch
        {
            CurrentUserApiStatus.Success => Results.Ok(new BrowserSessionResponse(
                true,
                new BrowserSessionUser(currentUser.User!.UserId, currentUser.User.Subject))),
            CurrentUserApiStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static async Task<IResult> LoginAsync(HttpContext httpContext, string? returnUrl)
    {
        if (!LocalReturnUrlValidator.TryNormalize(returnUrl, out var localReturnUrl))
        {
            return Results.BadRequest();
        }

        var authentication = await httpContext.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        if (authentication.Succeeded)
        {
            return Results.LocalRedirect(localReturnUrl);
        }

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = localReturnUrl },
            [BffAuthenticationExtensions.OidcScheme]);
    }

    private static async Task<IResult> GetAntiforgeryTokenAsync(HttpContext httpContext, IAntiforgery antiforgery)
    {
        var authentication = await httpContext.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        if (!authentication.Succeeded)
        {
            return Results.Unauthorized();
        }

        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }

    private static async Task<IResult> BeginLogoutAsync(
        HttpContext httpContext,
        ITicketStore ticketStore,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var authentication = await httpContext.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        if (!authentication.Succeeded
            || authentication.Ticket is null
            || !authentication.Ticket.Properties.Items.TryGetValue(
                InMemoryAuthenticationTicketStore.SessionKeyItem,
                out var sessionKey)
            || string.IsNullOrEmpty(sessionKey))
        {
            return Results.Unauthorized();
        }

        // Intent записывается в существующий server-side ticket без нового sign-in, чтобы
        // параллельно завершённая session не была создана заново.
        var ticket = authentication.Ticket;
        ticket.Properties.Items[LogoutIntentItem] = timeProvider.GetUtcNow()
            .Add(LogoutIntentLifetime)
            .ToString("o", CultureInfo.InvariantCulture);
        await ticketStore.RenewAsync(sessionKey, ticket, httpContext, cancellationToken);
        if (await ticketStore.RetrieveAsync(sessionKey, httpContext, cancellationToken) is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new LogoutResponse(LogoutCompletePath));
    }

    private static async Task<IResult> CompleteLogoutAsync(HttpContext httpContext, TimeProvider timeProvider)
    {
        var authentication = await httpContext.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        if (!authentication.Succeeded
            || authentication.Ticket is null
            || !HasActiveLogoutIntent(authentication.Ticket.Properties, timeProvider))
        {
            // Без подтверждённого POST /bff/auth/logout переход по ссылке не завершает session.
            return Results.LocalRedirect("/");
        }

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [BffAuthenticationExtensions.SessionScheme, BffAuthenticationExtensions.OidcScheme]);
    }

    private static bool HasActiveLogoutIntent(AuthenticationProperties properties, TimeProvider timeProvider) =>
        InMemoryAuthenticationTicketStore.TryGetLogoutIntentExpiry(properties, out var expiresAt)
        && expiresAt > timeProvider.GetUtcNow();

    [LoggerMessage(Level = LogLevel.Information, Message = "API повторно отклонил обновлённый access token; BFF session завершена.")]
    private static partial void LogSessionRejectedByApi(ILogger logger);
}
