using Microsoft.AspNetCore.Antiforgery;

namespace Intelligence.TradeSystem.Web.Security;

/// <summary>
/// Требует antiforgery request token для всех state-changing запросов к <c>/bff</c>.
/// SameSite cookie не считается достаточной CSRF-защитой.
/// </summary>
internal sealed partial class BffAntiforgeryMiddleware(
    RequestDelegate next,
    IAntiforgery antiforgery,
    ILogger<BffAntiforgeryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/bff") && IsStateChanging(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                LogValidationFailed(context.Request.Method, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }
        }

        await next(context);
    }

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "BFF отклонил {Method} {Path}: antiforgery token отсутствует или недействителен.")]
    private partial void LogValidationFailed(string method, PathString path);
}
