using Intelligence.TradeSystem.Bff.Api;
using Intelligence.TradeSystem.Bff.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Intelligence.TradeSystem.Bff.Authentication;

public static partial class BffAuthenticationExtensions
{
    public const string SessionScheme = "BffSession";
    public const string OidcScheme = "BffOidc";
    public const string SessionCookieName = "TradeSystem.Bff.Session";
    public const string AntiforgeryCookieName = "TradeSystem.Bff.Antiforgery";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    public const string TradeApiScope = "trade.api";
    public const string CallbackPath = "/signin-oidc";
    public const string SignedOutCallbackPath = "/signout-callback-oidc";

    internal static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        BffConfiguration configuration,
        IHostEnvironment environment)
    {
        var oidc = configuration.Oidc;
        var isLocal = BffConfigurationValidation.IsLocalEnvironment(environment);
        var securePolicy = isLocal ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services.AddSingleton(oidc);
        services.AddSingleton(configuration.Session);
        services.AddSingleton(configuration.Token);
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<ITicketStore, InMemoryAuthenticationTicketStore>();
        services.AddSingleton<BffTokenService>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = SessionScheme;
                options.DefaultChallengeScheme = OidcScheme;
            })
            .AddCookie(SessionScheme, options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = securePolicy;
                options.ExpireTimeSpan = configuration.Session.Lifetime;
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(OidcScheme, options =>
            {
                options.SignInScheme = SessionScheme;
                options.SignOutScheme = SessionScheme;
                options.Authority = oidc.Authority.AbsoluteUri;
                options.MetadataAddress = oidc.MetadataAddress.AbsoluteUri;
                options.RequireHttpsMetadata = !isLocal;
                options.BackchannelHttpHandler = new PublicIssuerBackchannelHandler(
                    oidc.Authority,
                    oidc.BackchannelBaseAddress)
                {
                    InnerHandler = new HttpClientHandler(),
                };
                options.ClientId = oidc.ClientId;
                options.ClientSecret = oidc.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.DisableTelemetry = true;
                options.Scope.Clear();
                options.Scope.Add(OpenIdConnectScope.OpenId);
                options.Scope.Add(OpenIdConnectScope.OfflineAccess);
                options.Scope.Add(TradeApiScope);
                options.CallbackPath = CallbackPath;
                options.SignedOutCallbackPath = SignedOutCallbackPath;
                options.SignedOutRedirectUri = "/";
                options.TokenValidationParameters.ValidIssuer = oidc.Authority.AbsoluteUri;
                options.TokenValidationParameters.NameClaimType = "name";

                if (isLocal)
                {
                    // SameSite=None без Secure браузеры отклоняют, поэтому HTTP localhost
                    // использует Lax: Identity и BFF на localhost считаются одним site.
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.NonceCookie.SameSite = SameSiteMode.Lax;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                }

                options.Events = new OpenIdConnectEvents
                {
                    OnRemoteFailure = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger(typeof(BffAuthenticationExtensions));
                        LogRemoteFailure(logger, context.Failure?.GetType().Name ?? "unknown");
                        context.Response.Redirect("/");
                        context.HandleResponse();
                        return Task.CompletedTask;
                    },
                };
            });

        services
            .AddOptions<CookieAuthenticationOptions>(SessionScheme)
            .Configure<ITicketStore>((options, ticketStore) => options.SessionStore = ticketStore);

        services.AddAuthorization();

        return services;
    }

    internal static IServiceCollection AddBffAntiforgery(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        var isLocal = BffConfigurationValidation.IsLocalEnvironment(environment);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = isLocal ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

        return services;
    }

    internal static IServiceCollection AddBffHttpClients(
        this IServiceCollection services,
        BffConfiguration configuration)
    {
        var oidc = configuration.Oidc;
        var tokenEndpointTimeout = configuration.Token.EndpointTimeout;

        // Повтор refresh_token grant может израсходовать ротируемый refresh token, поэтому
        // default resilience handlers из ServiceDefaults для этого client удаляются.
        // Другого API для удаления handlers из ConfigureHttpClientDefaults нет.
#pragma warning disable EXTEXP0001
        services
            .AddHttpClient(OAuthTokenRefreshClient.HttpClientName, client => client.Timeout = tokenEndpointTimeout)
            .AddHttpMessageHandler(() => new PublicIssuerBackchannelHandler(
                oidc.Authority,
                oidc.BackchannelBaseAddress))
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddSingleton<OAuthTokenRefreshClient>();

        services.AddHttpClient<CurrentUserApiClient>(client => client.BaseAddress = configuration.ApiBaseAddress);

        return services;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC callback завершился ошибкой {FailureType}; browser возвращён на публичную страницу.")]
    private static partial void LogRemoteFailure(ILogger logger, string failureType);
}
