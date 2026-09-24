using Intelligence.TradeSystem.Authentication.TestSeeder.Configuration;
using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Intelligence.TradeSystem.Authentication.TestSeeder;

public static class TestSeederServiceCollectionExtensions
{
    public static IServiceCollection AddAuthenticationTestSeeding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDataProtection();

        var settings = TestSeederSettings.FromConfiguration(configuration);
        services.AddSingleton(settings);
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
        services.AddScoped<AuthenticationTestSeeder>();

        return services;
    }
}
