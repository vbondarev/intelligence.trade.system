using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Api.Realtime.V1;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Users;
using Microsoft.AspNetCore.SignalR;

namespace Intelligence.TradeSystem.Api;

public static class ApiRealtimeExtensions
{
    public static IServiceCollection AddApiRealtime(this IServiceCollection services)
    {
        services
            .AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
                options.PayloadSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            });

        services.AddSingleton<IUserIdProvider, TradeUserIdProvider>();
        services.AddSingleton<UserUpdatesApplicationEventHandler>();
        services.AddSingleton<IApplicationEventHandler<PositionOpenedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());
        services.AddSingleton<IApplicationEventHandler<PositionChangedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());
        services.AddSingleton<IApplicationEventHandler<PositionClosedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());
        services.AddSingleton<IApplicationEventHandler<ExchangeAccountUpdatedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());
        services.AddSingleton<IApplicationEventHandler<ExchangeAccountSyncDegradedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());
        services.AddSingleton<IApplicationEventHandler<PortfolioUpdatedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());
        services.AddSingleton<IApplicationEventHandler<PositionEvaluationUpdatedEventV1>>(
            serviceProvider => serviceProvider.GetRequiredService<UserUpdatesApplicationEventHandler>());

        return services;
    }
}
