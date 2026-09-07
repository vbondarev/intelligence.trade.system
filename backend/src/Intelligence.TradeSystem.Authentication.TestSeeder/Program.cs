using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public static class Program
{
    public static async Task Main()
    {
        var builder = Host.CreateApplicationBuilder();
        var configuration = builder.Configuration;
        builder.Services.AddDataProtection();

        var connectionString = configuration.GetConnectionString(StartupExtensions.IdentityConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings__{StartupExtensions.IdentityConnectionStringName} must be configured.");
        var username = GetRequired(configuration, "TestSeeder:Username");
        var password = GetRequired(configuration, "TestSeeder:Password");
        var clientId = GetRequired(configuration, "TestSeeder:ClientId");
        var redirectUri = GetRequired(configuration, "TestSeeder:RedirectUri");

        builder.Services.AddIdentityPersistence(configuration);
        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            });

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(username);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = username
            };
            var result = await userManager.CreateAsync(user, password);
            EnsureSucceeded(result, "test user creation");
        }
        else if (!await userManager.CheckPasswordAsync(user, password))
        {
            throw new InvalidOperationException("The configured Compose smoke user already exists with a different password.");
        }

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applicationManager.FindByClientIdAsync(clientId) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                DisplayName = "Compose authentication smoke client",
                RedirectUris = { new Uri(redirectUri) },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + StartupExtensions.ApiScope
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange
                }
            });
        }
    }

    private static string GetRequired(ConfigurationManager configuration, string key) =>
        configuration[key]
        ?? throw new InvalidOperationException($"{key} must be configured.");

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
