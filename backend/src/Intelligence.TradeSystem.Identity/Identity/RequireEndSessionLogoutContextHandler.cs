using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Intelligence.TradeSystem.Identity.Identity;

/// <summary>
/// Отклоняет end-session request без <c>id_token_hint</c> или <c>post_logout_redirect_uri</c>.
/// </summary>
/// <remarks>
/// Стандарт допускает end-session без этих параметров, но тогда внешний сайт мог бы top-level
/// navigation с SameSite=Lax cookie завершить Identity SSO session в обход CSRF-защищённого
/// BFF logout. Handler выполняется после встроенной валидации OpenIddict, которая проверяет
/// регистрацию <c>post_logout_redirect_uri</c> у client, а <see cref="ValidateEndSessionRequestContext.IdentityTokenHintPrincipal"/>
/// заполняет только для <c>id_token_hint</c> с валидной подписью и authorized party. Невалидный
/// hint OpenIddict сам не отклоняет, поэтому handler требует наличие principal, а не только параметра.
/// </remarks>
public sealed class RequireEndSessionLogoutContextHandler : IOpenIddictServerHandler<ValidateEndSessionRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateEndSessionRequestContext>()
            .UseSingletonHandler<RequireEndSessionLogoutContextHandler>()
            .SetOrder(int.MaxValue - 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ValidateEndSessionRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(context.Request.IdTokenHint)
            || context.IdentityTokenHintPrincipal is null
            || string.IsNullOrEmpty(context.PostLogoutRedirectUri))
        {
            context.Reject(
                error: Errors.InvalidRequest,
                description: "The id_token_hint and post_logout_redirect_uri parameters are required.");
        }

        return ValueTask.CompletedTask;
    }
}
