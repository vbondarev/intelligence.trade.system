using System.Security.Claims;
using Intelligence.TradeSystem.Identity.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Intelligence.TradeSystem.Identity.Controllers;

public sealed class AuthorizationController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOpenIddictScopeManager scopeManager) : Controller
{
    private const string PrincipalTypeClaim = "trade_principal_type";
    private const string UserPrincipalType = "user";
    private const string PromptLoginMarker = "Identity.PromptLogin.AuthorizationRequest";

    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(HttpContext)
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;

        // Marker читается при каждом authorize-запросе, поэтому он одноразовый и не переживает
        // другой flow. Его значение привязано к конкретному authorization request.
        var promptLoginMarker = TempData[PromptLoginMarker] as string;
        var isAuthenticated = User.Identity?.IsAuthenticated == true;

        if (request.HasPromptValue(OpenIddictConstants.PromptValues.Login))
        {
            var reauthenticated = isAuthenticated
                && string.Equals(promptLoginMarker, returnUrl, StringComparison.Ordinal);
            if (!reauthenticated)
            {
                await signInManager.SignOutAsync();
                TempData[PromptLoginMarker] = returnUrl;
                return new ChallengeResult(
                    IdentityConstants.ApplicationScheme,
                    new AuthenticationProperties { RedirectUri = returnUrl });
            }
        }
        else if (!isAuthenticated)
        {
            return new ChallengeResult(
                IdentityConstants.ApplicationScheme,
                new AuthenticationProperties { RedirectUri = returnUrl });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
        identity.SetClaim(OpenIddictConstants.Claims.Name, user.UserName ?? user.Id.ToString());
        identity.SetClaim(PrincipalTypeClaim, UserPrincipalType);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopeManager.ListResourcesAsync(request.GetScopes()).ToListAsync());
        identity.SetDestinations(static claim =>
            claim.Type == PrincipalTypeClaim
                ? [OpenIddictConstants.Destinations.AccessToken]
                :
                [
                    OpenIddictConstants.Destinations.AccessToken,
                    OpenIddictConstants.Destinations.IdentityToken
                ]);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Завершает Identity SSO session. До controller доходит только end-session request,
    /// прошедший валидацию OpenIddict и <see cref="RequireEndSessionLogoutContextHandler"/>:
    /// с проверенным <c>id_token_hint</c> и зарегистрированным <c>post_logout_redirect_uri</c>,
    /// на который OpenIddict сам выполняет redirect.
    /// </summary>
    [HttpGet("~/connect/endsession")]
    [HttpPost("~/connect/endsession")]
    public async Task<IActionResult> EndSession()
    {
        await signInManager.SignOutAsync();

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
