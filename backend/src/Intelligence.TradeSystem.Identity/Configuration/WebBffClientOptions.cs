namespace Intelligence.TradeSystem.Identity.Configuration;

/// <summary>
/// Регистрация confidential OIDC client для BFF React-приложения.
/// </summary>
public sealed class WebBffClientOptions
{
    public const string SectionName = "Identity:WebBffClient";

    public bool Enabled { get; init; }

    public string ClientId { get; init; } = "trade-web-bff";

    /// <summary>
    /// Client secret передаётся только через environment/secret store и никогда не логируется.
    /// </summary>
    public string? ClientSecret { get; init; }

    public List<string> RedirectUris { get; init; } = [];

    public List<string> PostLogoutRedirectUris { get; init; } = [];

    public void Validate(IHostEnvironment environment)
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Identity:WebBffClient:ClientId должен быть задан.");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("Identity:WebBffClient:ClientSecret должен быть задан.");
        }

        ValidateUris(RedirectUris, "Identity:WebBffClient:RedirectUris", environment);
        ValidateUris(PostLogoutRedirectUris, "Identity:WebBffClient:PostLogoutRedirectUris", environment);
    }

    private static void ValidateUris(List<string> values, string key, IHostEnvironment environment)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException($"{key} должен содержать хотя бы один URI.");
        }

        var requireHttps = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.Fragment)
                || (requireHttps && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"{key} должен содержать только абсолютные URI без fragment; вне Development и Testing допускается только HTTPS.");
            }
        }
    }
}
