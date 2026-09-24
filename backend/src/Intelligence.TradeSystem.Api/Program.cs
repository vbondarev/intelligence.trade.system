using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Realtime.V1;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Api.Validation;
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
        builder.Services.AddRealtimeV1();
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiOpenApi();
        builder.Services.AddApiValidation();
        builder.Services.AddCurrentUserContext();
        builder.Services.AddSnapshotHealthEvaluation(builder.Configuration);
        builder.Services.AddTradeAuthentication(builder.Configuration, builder.Environment);

        var app = builder.Build();

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/", () => Results.Ok(new
        {
            Service = "Intelligence.TradeSystem.Api",
            Status = "Started",
        }));
        app.MapControllers();
        app.MapHub<UpdatesHub>(
                "/hubs/v1/updates",
                options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization(TradeAuthorization.UserPolicy);
        app.MapDefaultEndpoints();

        app.Run();
    }
}
