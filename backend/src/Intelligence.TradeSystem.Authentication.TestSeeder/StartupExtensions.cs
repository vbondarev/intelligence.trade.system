using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public static class StartupExtensions
{
    public static IServiceCollection AddAuthenticationTestSeeder(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDataProtection();

        _ = configuration.GetConnectionString(Identity.StartupExtensions.IdentityConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings__{Identity.StartupExtensions.IdentityConnectionStringName} must be configured.");
        var username = GetRequired(configuration, "TestSeeder:Username");
        var password = GetRequired(configuration, "TestSeeder:Password");
        var clientId = GetRequired(configuration, "TestSeeder:ClientId");
        var redirectUri = GetRequired(configuration, "TestSeeder:RedirectUri");

        services.AddIdentityPersistence(configuration);
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders();
        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            });

        services.AddSingleton(new TestSeederSettings(username, password, clientId, redirectUri));

        return services;
    }

    public static async Task SeedAuthenticationTestUserAsync(this IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<TestSeederSettings>();

        using var scope = serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var userManager = scopedProvider.GetRequiredService<UserManager<ApplicationUser>>();
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
            throw new InvalidOperationException("The configured Compose smoke user already exists with a different password.");
        }

        var applicationManager = scopedProvider.GetRequiredService<IOpenIddictApplicationManager>();
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

    private static string GetRequired(IConfiguration configuration, string key) =>
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

    private sealed record TestSeederSettings(
        string Username,
        string Password,
        string ClientId,
        string RedirectUri);
}
