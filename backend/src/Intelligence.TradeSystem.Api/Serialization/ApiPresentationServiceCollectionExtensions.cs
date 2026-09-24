using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Api.Serialization;

namespace Intelligence.TradeSystem.Api;

public static class ApiPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddApiPresentation(this IServiceCollection services)
    {
        services
            .AddControllers(options =>
            {
                options.OutputFormatters.Insert(0, new V1JsonOutputFormatter());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(allowIntegerValues: false));
            });

        return services;
    }
}
