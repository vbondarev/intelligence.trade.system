using Intelligence.TradeSystem.ServiceDefaults;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Intelligence.TradeSystem.Bff.Endpoints;
using Intelligence.TradeSystem.Bff.Security;

namespace Intelligence.TradeSystem.Bff;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        var configuration = BffConfiguration.Load(builder.Configuration, builder.Environment);
        builder.Services.AddBffForwardedHeaders(configuration);
        builder.Services.AddBffAuthentication(configuration, builder.Environment);
        builder.Services.AddBffAntiforgery(builder.Environment);
        builder.Services.AddBffHttpClients(configuration);

        var app = builder.Build();

        // OIDC redirect URIs строятся из request scheme/host, поэтому public context frontend
        // proxy должен быть применён до authentication middleware.
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<BffAntiforgeryMiddleware>();
        app.MapAuthEndpoints();
        app.MapDefaultEndpoints();

        app.Run();
    }
}
