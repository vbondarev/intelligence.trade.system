using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Intelligence.TradeSystem.Identity.Identity;

/// <summary>
/// Отклоняет end-session request без <c>id_token_hint</c> или <c>post_logout_redirect_uri</c>,
/// а также request, чей <c>id_token_hint</c> выдан не пользователю текущей Identity SSO session.
/// </summary>
/// <remarks>
/// Стандарт допускает end-session без этих параметров, но тогда внешний сайт мог бы top-level
/// navigation с SameSite=Lax cookie завершить Identity SSO session в обход CSRF-защищённого
/// BFF logout. Handler выполняется после встроенной валидации OpenIddict, которая проверяет
/// регистрацию <c>post_logout_redirect_uri</c> у client, а <see cref="ValidateEndSessionRequestContext.IdentityTokenHintPrincipal"/>
/// заполняет только для <c>id_token_hint</c> с валидной подписью и authorized party. Невалидный
/// hint OpenIddict сам не отклоняет, поэтому handler требует наличие principal, а не только параметра.
/// <para>
/// OpenIddict не связывает subject hint с browser session: собственный валидный ID token атакующего
/// иначе завершил бы чужую SSO session. Поэтому при активной Identity session её user id обязан
/// совпадать с <c>sub</c> hint. Без активной session завершать нечего, и request проходит, чтобы
/// BFF logout по-прежнему завершался redirect на зарегистрированный <c>post_logout_redirect_uri</c>.
/// </para>
/// </remarks>
public sealed class RequireEndSessionLogoutContextHandler(IOptions<IdentityOptions> identityOptions)
    : IOpenIddictServerHandler<ValidateEndSessionRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateEndSessionRequestContext>()
            .UseSingletonHandler<RequireEndSessionLogoutContextHandler>()
            .SetOrder(int.MaxValue - 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateEndSessionRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(context.Request.IdTokenHint)
            || context.IdentityTokenHintPrincipal is null
            || string.IsNullOrEmpty(context.PostLogoutRedirectUri))
        {
            context.Reject(
                error: Errors.InvalidRequest,
                description: "The id_token_hint and post_logout_redirect_uri parameters are required.");
            return;
        }

        var httpContext = context.Transaction.GetHttpRequest()?.HttpContext
            ?? throw new InvalidOperationException("The ASP.NET Core request cannot be retrieved.");

        // OpenIddict обрабатывает request до того, как authentication middleware заполнит
        // HttpContext.User, поэтому Identity cookie аутентифицируется явно.
        var session = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!session.Succeeded)
        {
            return;
        }

        var sessionSubject = session.Principal.FindFirst(identityOptions.Value.ClaimsIdentity.UserIdClaimType)?.Value;
        var hintSubject = context.IdentityTokenHintPrincipal.GetClaim(Claims.Subject);
        if (string.IsNullOrEmpty(sessionSubject)
            || string.IsNullOrEmpty(hintSubject)
            || !string.Equals(sessionSubject, hintSubject, StringComparison.Ordinal))
        {
            context.Reject(
                error: Errors.InvalidRequest,
                description: "The id_token_hint does not match the current user session.");
        }
    }
}
