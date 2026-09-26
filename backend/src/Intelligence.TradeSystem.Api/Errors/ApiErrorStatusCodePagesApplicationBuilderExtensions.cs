using Microsoft.AspNetCore.Diagnostics;

namespace Intelligence.TradeSystem.Api.Errors;

internal static class ApiErrorStatusCodePagesApplicationBuilderExtensions
{
    public static IApplicationBuilder UseApiErrorStatusCodePages(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var httpContext = statusCodeContext.HttpContext;
            if (!httpContext.Request.Path.StartsWithSegments("/api/v1"))
            {
                return;
            }

            var descriptor = httpContext.Response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized => ApiErrorDescriptors.AuthenticationRequired,
                StatusCodes.Status403Forbidden => ApiErrorDescriptors.AccessForbidden,
                _ => null,
            };

            if (descriptor is null)
            {
                return;
            }

            var problemDetailsService = httpContext.RequestServices
                .GetRequiredService<IProblemDetailsService>();
            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = ApiProblemDetails.Create(httpContext, descriptor),
            });
        });
}
