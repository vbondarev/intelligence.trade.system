using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Errors;

public static class ErrorHandlingServiceCollectionExtensions
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ApiProblemDetails.Customize;
        });

        services.AddExceptionHandler<ApiExceptionHandler>();
        services.Configure<ExceptionHandlerOptions>(options =>
        {
            options.SuppressDiagnosticsCallback = context =>
                ApiExceptionHandler.ShouldSuppressDiagnostics(
                    context.Exception,
                    context.HttpContext.RequestAborted.IsCancellationRequested);
        });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var detail = context.ModelState.Values
                        .SelectMany(state => state.Errors)
                        .Select(error => error.ErrorMessage)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                    ?? "The request could not be processed.";
                var problemDetails = ApiProblemDetails.CreateValidation(context.HttpContext, detail);
                ApiProblemDetails.AddModelStateErrors(problemDetails, context.ModelState);

                return new BadRequestObjectResult(problemDetails);
            };
        });

        return services;
    }
}
