namespace Intelligence.TradeSystem.Bff.Realtime;

public sealed class RealtimeOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/bff/hubs/v1/updates", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method))
        {
            var origins = context.Request.Headers.Origin;
            if (context.Request.Query.ContainsKey("access_token")
                || origins.Count != 1
                || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
                || origin.UserInfo.Length != 0
                || origin.AbsolutePath != "/"
                || origin.Query.Length != 0
                || origin.Fragment.Length != 0
                || !Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host}", UriKind.Absolute, out var publicOrigin)
                || !string.Equals(origin.Scheme, publicOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(origin.IdnHost, publicOrigin.IdnHost, StringComparison.OrdinalIgnoreCase)
                || origin.Port != publicOrigin.Port)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next(context);
    }
}
