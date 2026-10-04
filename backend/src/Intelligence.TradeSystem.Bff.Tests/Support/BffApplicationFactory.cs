using System.Globalization;
using System.Net;
using System.Security.Claims;
using Intelligence.TradeSystem.Bff.Api;
using Intelligence.TradeSystem.Bff.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Intelligence.TradeSystem.Bff.Tests.Support;

/// <summary>
/// Поднимает настоящий composition root BFF, заменяя только внешние границы:
/// Identity token endpoint, business API, OIDC discovery и время.
/// </summary>
internal sealed class BffApplicationFactory : WebApplicationFactory<Program>
{
    public const string Authority = "http://identity.test/";
    public const string ApiBaseAddress = "http://api.test/";
    public const string ClientId = "trade-web-bff-test";
    public const string SignInPath = "/test/sign-in";
    public const string SpaIndexMarker = "bff-spa-index";

    public static readonly Uri BaseAddress = new("http://localhost/");
    public static readonly string ClientSecret = "bff-test-secret-" + Guid.NewGuid().ToString("N");

    public BffApplicationFactory()
    {
        WebRoot = Path.Combine(Path.GetTempPath(), "bff-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WebRoot);
        File.WriteAllText(
            Path.Combine(WebRoot, "index.html"),
            $"<!doctype html><html><body>{SpaIndexMarker}</body></html>");
    }

    public MutableTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public RecordingHttpHandler TokenEndpoint { get; } = new();

    public RecordingHttpHandler Api { get; } = new();

    public string WebRoot { get; }

    public static OpenIdConnectConfiguration OidcConfiguration { get; } = new()
    {
        Issuer = Authority,
        AuthorizationEndpoint = Authority + "connect/authorize",
        TokenEndpoint = Authority + "connect/token",
        EndSessionEndpoint = Authority + "connect/endsession",
    };

    public BffBrowser CreateBrowser() => new(this);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Bff:Oidc:Authority", Authority);
        builder.UseSetting("Bff:Oidc:ClientId", ClientId);
        builder.UseSetting("Bff:Oidc:ClientSecret", ClientSecret);
        builder.UseSetting("Bff:Api:BaseAddress", ApiBaseAddress);
        builder.UseWebRoot(WebRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            services.PostConfigure<OpenIdConnectOptions>(
                BffAuthenticationExtensions.OidcScheme,
                options => options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(OidcConfiguration));

            services.AddHttpClient(OAuthTokenRefreshClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => TokenEndpoint)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services.AddHttpClient<CurrentUserApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Api)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

            services.AddSingleton<IStartupFilter, TestSignInStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(WebRoot))
        {
            Directory.Delete(WebRoot, recursive: true);
        }
    }

    /// <summary>
    /// Test-only аналог успешного OIDC callback: создаёт BFF session с заданными tokens,
    /// не обращаясь к Identity.
    /// </summary>
    private sealed class TestSignInStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(SignInPath, branch => branch.Run(async context =>
            {
                var query = context.Request.Query;
                var timeProvider = context.RequestServices.GetRequiredService<TimeProvider>();
                var lifetime = TimeSpan.FromSeconds(double.Parse(query["lifetime"]!, CultureInfo.InvariantCulture));

                var tokens = new List<AuthenticationToken>
                {
                    new() { Name = "access_token", Value = query["access"]! },
                    new()
                    {
                        Name = "expires_at",
                        Value = timeProvider.GetUtcNow().Add(lifetime).ToString("o", CultureInfo.InvariantCulture),
                    },
                };
                if (!string.IsNullOrEmpty(query["refresh"]))
                {
                    tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = query["refresh"]! });
                }

                if (!string.IsNullOrEmpty(query["id"]))
                {
                    tokens.Add(new AuthenticationToken { Name = "id_token", Value = query["id"]! });
                }

                var properties = new AuthenticationProperties();
                properties.StoreTokens(tokens);

                var claims = new List<Claim>();
                if (!string.IsNullOrEmpty(query["sub"]))
                {
                    claims.Add(new Claim("sub", query["sub"]!));
                }

                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", "role"));
                await context.SignInAsync(BffAuthenticationExtensions.SessionScheme, principal, properties);
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            }));

            next(app);
        };
    }
}
