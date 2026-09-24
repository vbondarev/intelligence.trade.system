using Intelligence.TradeSystem.Api.Realtime.V1;
using Intelligence.TradeSystem.Api.Authentication;

namespace Intelligence.TradeSystem.Api;

public static class ApiEndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Ok(new
        {
            Service = "Intelligence.TradeSystem.Api",
            Status = "Started",
        }));

        app.MapControllers();
        app.MapHub<UpdatesHub>("/hubs/v1/updates", options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization(TradeAuthorization.UserPolicy);

        return app;
    }
}
