using Intelligence.TradeSystem.Authentication.TestSeeder.Configuration;
using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public sealed class AuthenticationTestSeeder(
    UserManager<ApplicationUser> userManager,
    IOpenIddictApplicationManager applicationManager,
    TestSeederSettings settings)
{
    public async Task SeedAsync()
    {
        var user = await userManager.FindByNameAsync(settings.Username);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = settings.Username,
            };
            var result = await userManager.CreateAsync(user, settings.Password);
            EnsureSucceeded(result, "test user creation");
        }
        else if (!await userManager.CheckPasswordAsync(user, settings.Password))
        {
            throw new InvalidOperationException(
                "The configured Compose smoke user already exists with a different password.");
        }

        if (await applicationManager.FindByClientIdAsync(settings.ClientId) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = settings.ClientId,
                DisplayName = "Compose authentication smoke client",
                RedirectUris = { new Uri(settings.RedirectUri) },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + Identity.StartupExtensions.ApiScope,
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange,
                },
            });
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Identity {operation} failed: {string.Join(", ", result.Errors.Select(error => error.Description))}");
    }
}
