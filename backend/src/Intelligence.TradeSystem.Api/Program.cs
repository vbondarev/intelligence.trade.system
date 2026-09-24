using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Exchanges;
using Intelligence.TradeSystem.Infrastructure;
using Intelligence.TradeSystem.ServiceDefaults;

namespace Intelligence.TradeSystem.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services.AddApplication();
        builder.Services.AddBybitExchange();
        builder.Services.AddInfrastructure(
            builder.Configuration,
            builder.Environment.ContentRootPath);
        builder.Services.AddPublicMarketSnapshotCaching(builder.Configuration);
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddExchangeAccountBackgroundSynchronization(builder.Configuration);
            builder.Services.AddApplicationEventOutboxDispatcher(builder.Configuration);
        }

        builder.Services.AddApiPresentation();
        builder.Services.AddApiRealtime();
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiValidation();
        builder.Services.AddSnapshotHealthEvaluation(builder.Configuration);
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);

        var app = builder.Build();

        app.UseApiExceptionHandling();

        if (app.Environment.IsDevelopment())
        {
            app.UseApiSwagger();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapApiEndpoints();
        app.MapDefaultEndpoints();

        app.Run();
    }
}
