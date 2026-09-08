using System.Text.Json.Serialization;
using Intelligence.TradeSystem.ServiceDefaults;

namespace Intelligence.TradeSystem.Identity;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services.AddIdentityPersistence(builder.Configuration);
        builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
        builder.Services
            .AddControllersWithViews()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            });

        var app = builder.Build();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/", () => Results.Ok(new
        {
            Service = "Intelligence.TradeSystem.Identity",
            Status = "Started",
        }));
        app.MapDefaultEndpoints();

        app.Run();
    }
}
