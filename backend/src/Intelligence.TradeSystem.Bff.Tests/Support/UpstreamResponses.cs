using System.Net;
using System.Text.Json;

namespace Intelligence.TradeSystem.Bff.Tests.Support;

internal static class UpstreamResponses
{
    public static readonly Guid UserId = Guid.Parse("4f0f4b1e-6f55-4c4b-9f56-0c0d5a1b2c3d");

    public static HttpResponseMessage CurrentUser(string subject = "user-subject") =>
        RecordingHttpHandler.Json(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { userId = UserId, subject, authenticated = true }));

    public static HttpResponseMessage TokenSuccess(
        string accessToken,
        string? refreshToken = null,
        long expiresIn = 3600,
        string? idToken = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["access_token"] = accessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = expiresIn,
        };
        if (refreshToken is not null)
        {
            payload["refresh_token"] = refreshToken;
        }

        if (idToken is not null)
        {
            payload["id_token"] = idToken;
        }

        return RecordingHttpHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(payload));
    }

    public static HttpResponseMessage TokenError(HttpStatusCode statusCode, string error) =>
        RecordingHttpHandler.Json(statusCode, JsonSerializer.Serialize(new { error }));
}
