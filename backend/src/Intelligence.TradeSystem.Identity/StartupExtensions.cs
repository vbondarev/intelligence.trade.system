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
        var serverOptions = configuration
            .GetSection(IdentityServerOptions.SectionName)
            .Get<IdentityServerOptions>() ?? new IdentityServerOptions();
        serverOptions.Validate();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.AllowedForNewUsers = serverOptions.AllowedForNewUsers;
                options.Lockout.MaxFailedAccessAttempts = serverOptions.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = serverOptions.DefaultLockoutTimeSpan;
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
                options.SetAuthorizationEndpointUris(
                    new[]
                    {
                        new Uri(issuer, "/connect/authorize"),
                        new Uri("/connect/authorize", UriKind.Relative)
                    });
                options.SetTokenEndpointUris(
                    new[]
                    {
                        new Uri(issuer, "/connect/token"),
                        new Uri("/connect/token", UriKind.Relative)
                    });
                options.SetConfigurationEndpointUris(
                    new[]
                    {
                        new Uri(issuer, "/.well-known/openid-configuration"),
                        new Uri("/.well-known/openid-configuration", UriKind.Relative)
                    });
                options.SetJsonWebKeySetEndpointUris(
                    new[]
                    {
                        new Uri(issuer, "/.well-known/jwks"),
                        new Uri("/.well-known/jwks", UriKind.Relative)
                    });
                options.AllowAuthorizationCodeFlow();
                options.AllowRefreshTokenFlow();
                options.RegisterScopes(ApiScope);
                options.RequireProofKeyForCodeExchange();
                options.Configure(server =>
                {
                    server.CodeChallengeMethods.Clear();
                    server.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                });
                options.DisableAccessTokenEncryption();
                options.SetAccessTokenLifetime(serverOptions.AccessTokenLifetime);

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
        if (serverOptions.SigningCertificates.Count > 0)
        {
            options.AddSigningCertificates(serverOptions.SigningCertificates.Select(LoadCertificate));
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
        if (serverOptions.SigningCertificates.Count == 0
            || string.IsNullOrWhiteSpace(serverOptions.EncryptionCertificatePath))
        {
            throw new InvalidOperationException(
                "Production Identity requires persistent signing certificates and an encryption certificate path.");
        }

        options
            .AddSigningCertificates(serverOptions.SigningCertificates.Select(LoadCertificate))
            .AddEncryptionCertificate(LoadCertificate(
                serverOptions.EncryptionCertificatePath,
                serverOptions.EncryptionCertificatePassword));
    }

    private static X509Certificate2 LoadCertificate(CertificateOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new InvalidOperationException(
                "Every Identity:SigningCertificates entry must specify a certificate Path.");
        }

        if (!File.Exists(options.Path))
        {
            throw new InvalidOperationException(
                $"Configured Identity certificate was not found: {options.Path}");
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.Path,
            options.Password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.MachineKeySet);
        ValidateCertificate(certificate, options.Path);
        return certificate;
    }

    private static X509Certificate2 LoadCertificate(string path, string? password)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.MachineKeySet);
        ValidateCertificate(certificate, path);
        return certificate;
    }

    private static void ValidateCertificate(X509Certificate2 certificate, string path)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"Configured Identity signing certificate has no private key: {path}");
        }

        var now = DateTime.UtcNow;
        if (certificate.NotBefore.ToUniversalTime() > now
            || certificate.NotAfter.ToUniversalTime() <= now)
        {
            throw new InvalidOperationException(
                $"Configured Identity signing certificate is outside its validity period: {path}");
        }
    }
}
