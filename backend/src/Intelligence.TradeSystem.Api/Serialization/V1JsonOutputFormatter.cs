using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace Intelligence.TradeSystem.Api.Serialization;

internal sealed class V1JsonOutputFormatter : TextOutputFormatter
{
    public V1JsonOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/problem+json"));
        SupportedEncodings.Add(Encoding.UTF8);
    }

    public override bool CanWriteResult(OutputFormatterCanWriteContext context) =>
        IsV1Request(context.HttpContext) && base.CanWriteResult(context);

    public override Task WriteResponseBodyAsync(
        OutputFormatterWriteContext context,
        Encoding _)
    {
        var objectType = context.ObjectType
            ?? context.Object?.GetType()
            ?? typeof(object);

        return JsonSerializer.SerializeAsync(
            context.HttpContext.Response.Body,
            context.Object,
            objectType,
            V1JsonSerializerOptions.Default,
            context.HttpContext.RequestAborted);
    }

    private static bool IsV1Request(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value;

        return string.Equals(path, "/api/v1", StringComparison.OrdinalIgnoreCase)
            || path?.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) == true;
    }
}
