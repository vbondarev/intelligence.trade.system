using Intelligence.TradeSystem.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Services;

public static class SnapshotHealthServiceCollectionExtensions
{
    public static IServiceCollection AddSnapshotHealthEvaluation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<
            IValidateOptions<SnapshotFreshnessOptions>,
            SnapshotFreshnessOptionsValidator>();
        services
            .AddOptions<SnapshotFreshnessOptions>()
            .Bind(configuration.GetSection(SnapshotFreshnessOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<ISnapshotHealthEvaluator, SnapshotHealthEvaluator>();

        return services;
    }
}
