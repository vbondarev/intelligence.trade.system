using Intelligence.TradeSystem.ServiceDefaults;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Intelligence.TradeSystem.Bff.Endpoints;
using Intelligence.TradeSystem.Bff.Security;
using Intelligence.TradeSystem.Bff.Realtime;
using Microsoft.AspNetCore.Http.Connections;

namespace Intelligence.TradeSystem.Bff;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        var configuration = BffConfiguration.Load(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(configuration);
        builder.Services.AddBffForwardedHeaders(configuration);
        builder.Services.AddBffAuthentication(configuration, builder.Environment);
        builder.Services.AddBffAntiforgery(builder.Environment);
        builder.Services.AddBffHttpClients(configuration);
        builder.Services.AddSignalR();
        builder.Services.AddAuthorizationBuilder().AddPolicy("BrowserRealtime", policy => policy
            .AddAuthenticationSchemes(BffAuthenticationExtensions.SessionScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !string.IsNullOrWhiteSpace(context.User.FindFirst("sub")?.Value)));

        var app = builder.Build();

        // OIDC redirect URIs строятся из request scheme/host, поэтому public context frontend
        // proxy должен быть применён до authentication middleware.
        app.UseForwardedHeaders();
        app.UseMiddleware<RealtimeOriginMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<BffAntiforgeryMiddleware>();
        app.MapHub<BrowserUpdatesHub>("/bff/hubs/v1/updates", options => options.Transports = HttpTransportType.WebSockets)
            .RequireAuthorization();
        app.MapAuthEndpoints();
        app.MapExchangeAccountEndpoints();
        app.MapPortfolioEndpoints();
        app.MapPositionEndpoints();

        // Неизвестный /bff/** path не должен попадать в SPA fallback frontend или в API.
        // Контракт требует no-store для всех ответов /bff/me/**, включая неизвестные paths.
        app.Map("/bff/{**path}", (HttpContext httpContext) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            return Results.NotFound();
        });
        app.MapDefaultEndpoints();

        app.Run();
    }
}
