using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Intelligence.TradeSystem.Api.Errors;

internal sealed class V1AuthorizationMiddlewareResultHandler(
    IProblemDetailsService problemDetailsService) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1"))
        {
            await _defaultHandler
                .HandleAsync(next, context, policy, authorizeResult)
                .ConfigureAwait(false);
            return;
        }

        await _defaultHandler
            .HandleAsync(next, context, policy, authorizeResult)
            .ConfigureAwait(false);

        var descriptor = authorizeResult.Challenged
            ? ApiErrorDescriptors.AuthenticationRequired
            : authorizeResult.Forbidden
                ? ApiErrorDescriptors.AccessForbidden
                : null;
        if (descriptor is null)
        {
            return;
        }

        context.Response.StatusCode = descriptor.StatusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = ApiProblemDetails.Create(context, descriptor),
        }).ConfigureAwait(false);
    }
}
