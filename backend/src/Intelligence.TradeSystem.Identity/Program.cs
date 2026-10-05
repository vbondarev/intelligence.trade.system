using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Intelligence.TradeSystem.ServiceDefaults;
using Microsoft.Extensions.WebEncoders;

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
        // Razor по умолчанию превращает весь текст вне BasicLatin в numeric character references;
        // кириллица страниц Identity выводится как есть, HTML-спецсимволы по-прежнему экранируются.
        builder.Services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic));

        var app = builder.Build();

        app.UseStaticFiles();
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
