namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Ответ business API, возвращаемый browser без переклассификации: сохраняются HTTP status,
/// JSON media type и тело, включая ProblemDetails с machine-readable полями.
/// </summary>
/// <remarks>
/// Тип не является record, чтобы тело ответа не попадало в автоматический <c>ToString</c>.
/// </remarks>
internal sealed class ApiForwardResponse : IResult
{
    private readonly byte[] body;

    private ApiForwardResponse(int statusCode, string? contentType, byte[] body)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        this.body = body;
    }

    public int StatusCode { get; }

    public string? ContentType { get; }

    public ReadOnlyMemory<byte> Body => body;

    /// <summary>Ответ, сформированный самим BFF, без тела.</summary>
    public static ApiForwardResponse Status(int statusCode) => new(statusCode, null, []);

    public static ApiForwardResponse FromApi(int statusCode, string? contentType, byte[] body) =>
        IsJson(contentType) && body.Length > 0
            ? new ApiForwardResponse(statusCode, contentType, body)
            : Status(statusCode);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCode;
        response.Headers.CacheControl = "no-store";
        if (body.Length == 0)
        {
            return;
        }

        response.ContentType = ContentType;
        response.ContentLength = body.Length;
        await response.Body.WriteAsync(body, httpContext.RequestAborted);
    }

    private static bool IsJson(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)
            || !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            || mediaType.MediaType is null)
        {
            return false;
        }

        return string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.MediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
