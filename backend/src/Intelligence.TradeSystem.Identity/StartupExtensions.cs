using System.Security.Cryptography.X509Certificates;
using Intelligence.TradeSystem.Identity.Configuration;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Identity;

public static class StartupExtensions
{
    public const string IdentityConnectionStringName = "TradeSystemIdentity";
    public const string ApiResource = "intelligence-trade-api";
    public const string ApiScope = "trade.api";

    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(IdentityConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{IdentityConnectionStringName} must be configured for the Identity host.");
        }

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(IdentityDbContext).Assembly.GetName().Name)));

        services
            .AddHealthChecks()
            .AddDbContextCheck<IdentityDbContext>("identity-postgresql");

        return services;
    }

    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();
        services.AddHostedService<IdentityScopeSeeder>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "TradeSystem.Identity";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            options.LoginPath = "/account/login";
            options.ReturnUrlParameter = "returnUrl";
        });

        var serverOptions = configuration
            .GetSection(IdentityServerOptions.SectionName)
            .Get<IdentityServerOptions>() ?? new IdentityServerOptions();
        var issuer = ResolveIssuer(serverOptions, environment);

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            })
            .AddServer(options =>
            {
                options.SetIssuer(issuer);
                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetTokenEndpointUris("/connect/token");
                options.AllowAuthorizationCodeFlow();
                options.AllowRefreshTokenFlow();
                options.RequireProofKeyForCodeExchange();
                options.Configure(server =>
                {
                    server.CodeChallengeMethods.Clear();
                    server.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                });
                options.DisableAccessTokenEncryption();

                if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
                {
                    ConfigureDevelopmentCredentials(options, serverOptions);
                }
                else
                {
                    ConfigureProductionCredentials(options, serverOptions);
                }

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough();

                if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
                {
                    options.UseAspNetCore().DisableTransportSecurityRequirement();
                }
            });

        return services;
    }

    private static Uri ResolveIssuer(IdentityServerOptions options, IHostEnvironment environment)
    {
        var value = options.Issuer;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Identity:Issuer must be configured with a stable HTTPS URL outside Development and Testing.");
            }

            value = "http://localhost:5001";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var issuer)
            || issuer is null
            || issuer.Scheme is not ("http" or "https")
            || (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && issuer.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Identity:Issuer must be an absolute HTTPS URL in Production.");
        }

        return issuer;
    }

    private static void ConfigureDevelopmentCredentials(
        OpenIddictServerBuilder options,
        IdentityServerOptions serverOptions)
    {
        if (!string.IsNullOrWhiteSpace(serverOptions.SigningCertificatePath))
        {
            options.AddSigningCertificate(LoadCertificate(
                serverOptions.SigningCertificatePath,
                serverOptions.SigningCertificatePassword));
        }
        else
        {
            options.AddDevelopmentSigningCertificate();
        }

        if (!string.IsNullOrWhiteSpace(serverOptions.EncryptionCertificatePath))
        {
            options.AddEncryptionCertificate(LoadCertificate(
                serverOptions.EncryptionCertificatePath,
                serverOptions.EncryptionCertificatePassword));
        }
        else
        {
            options.AddDevelopmentEncryptionCertificate();
        }
    }

    private static void ConfigureProductionCredentials(
        OpenIddictServerBuilder options,
        IdentityServerOptions serverOptions)
    {
        if (string.IsNullOrWhiteSpace(serverOptions.SigningCertificatePath)
            || string.IsNullOrWhiteSpace(serverOptions.EncryptionCertificatePath))
        {
            throw new InvalidOperationException(
                "Production Identity requires persistent signing and encryption certificate paths.");
        }

        options
            .AddSigningCertificate(LoadCertificate(
                serverOptions.SigningCertificatePath,
                serverOptions.SigningCertificatePassword))
            .AddEncryptionCertificate(LoadCertificate(
                serverOptions.EncryptionCertificatePath,
                serverOptions.EncryptionCertificatePassword));
    }

    private static X509Certificate2 LoadCertificate(string path, string? password)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Configured Identity certificate was not found: {path}");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.MachineKeySet);
    }
}
