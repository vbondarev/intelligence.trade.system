namespace Intelligence.TradeSystem.Bff.Configuration;

internal static class BffConfigurationValidation
{
    /// <summary>
    /// Development и Testing допускают HTTP localhost; остальные окружения считаются публичными
    /// и требуют HTTPS для внешних адресов и Secure cookies.
    /// </summary>
    public static bool IsLocalEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static Uri RequireAbsoluteUri(string? value, string key, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} должен быть задан.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"{key} должен быть абсолютным HTTP(S) URL.");
        }

        if (!IsLocalEnvironment(environment) && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{key} должен быть абсолютным HTTPS URL вне Development и Testing.");
        }

        return uri;
    }
}
