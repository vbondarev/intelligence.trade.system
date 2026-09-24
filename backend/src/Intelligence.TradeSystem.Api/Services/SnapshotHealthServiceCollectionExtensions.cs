using Intelligence.TradeSystem.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Services;

public static class SnapshotHealthServiceCollectionExtensions
{
    public static IServiceCollection AddSnapshotHealthEvaluation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var freshnessOptions = configuration
            .GetSection(SnapshotFreshnessOptions.SectionName)
            .Get<SnapshotFreshnessOptions>() ?? SnapshotFreshnessOptions.Default;

        services.AddOptions<SnapshotFreshnessOptions>().Configure(_ => { });
        services.AddSingleton(freshnessOptions);
        services.AddSingleton<IOptions<SnapshotFreshnessOptions>>(serviceProvider =>
            Options.Create(serviceProvider.GetRequiredService<SnapshotFreshnessOptions>()));
        services.AddSingleton<ISnapshotHealthEvaluator, SnapshotHealthEvaluator>();

        return services;
    }
}
