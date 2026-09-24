using FluentValidation;
using Intelligence.TradeSystem.Api.Contracts;

namespace Intelligence.TradeSystem.Api.Validation;

public static class ValidationServiceCollectionExtensions
{
    public static IServiceCollection AddApiValidation(this IServiceCollection services)
    {
        services.AddScoped<IValidator<SnapshotAnalysisRequest>, SnapshotAnalysisRequestValidator>();
        services.AddScoped<IValidator<LlmPayloadRequest>, LlmPayloadRequestValidator>();

        return services;
    }
}
