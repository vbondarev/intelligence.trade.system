using Intelligence.TradeSystem.ServiceDefaults;
using Intelligence.TradeSystem.Web.Authentication;
using Intelligence.TradeSystem.Web.Configuration;
using Intelligence.TradeSystem.Web.Endpoints;
using Intelligence.TradeSystem.Web.Security;

namespace Intelligence.TradeSystem.Web;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        var configuration = WebBffConfiguration.Load(builder.Configuration, builder.Environment);
        builder.Services.AddWebAuthentication(configuration, builder.Environment);
        builder.Services.AddWebAntiforgery(builder.Environment);
        builder.Services.AddWebHttpClients(configuration);

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<BffAntiforgeryMiddleware>();
        app.MapAuthEndpoints();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
        app.MapDefaultEndpoints();

        app.Run();
    }
}
