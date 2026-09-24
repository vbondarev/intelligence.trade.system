using FluentValidation;
using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Api.Validation;
using Intelligence.TradeSystem.Application.Users;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api;

public static class ApiValidationExtensions
{
    public static IServiceCollection AddApiValidation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, ClaimsPrincipalCurrentUserContext>();
        services.AddScoped<IValidator<SnapshotAnalysisRequest>, SnapshotAnalysisRequestValidator>();
        services.AddScoped<IValidator<LlmPayloadRequest>, LlmPayloadRequestValidator>();

        return services;
    }

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
