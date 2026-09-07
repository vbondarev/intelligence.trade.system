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
    IOpenIddictScopeManager scopeManager) : Controller
{
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(HttpContext)
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
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
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopeManager.ListResourcesAsync(request.GetScopes()).ToListAsync());
        identity.SetDestinations(static _ =>
        [
            OpenIddictConstants.Destinations.AccessToken,
            OpenIddictConstants.Destinations.IdentityToken
        ]);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
