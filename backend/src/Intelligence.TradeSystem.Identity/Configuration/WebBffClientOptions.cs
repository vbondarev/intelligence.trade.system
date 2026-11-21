namespace Intelligence.TradeSystem.Identity.Configuration;

/// <summary>
/// Регистрация confidential OIDC client для BFF React-приложения.
/// </summary>
public sealed class WebBffClientOptions
{
    public const string SectionName = "Identity:WebBffClient";

    /// <summary>
    /// Канонический OIDC client id G-01. Это invariant, а не environment-specific setting.
    /// </summary>
    public const string CanonicalClientId = "trade-web-bff";

    public bool Enabled { get; init; }

    /// <summary>
    /// Должен быть равен <see cref="CanonicalClientId"/> и совпадать с <c>Bff:Oidc:ClientId</c>.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Client secret передаётся только через environment/secret store и никогда не логируется.
    /// </summary>
    public string? ClientSecret { get; init; }

    public List<string> RedirectUris { get; init; } = [];

    public List<string> PostLogoutRedirectUris { get; init; } = [];

    public string? PublicOrigin { get; init; }

    internal Uri? LoginContinuation { get; private set; }

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

        if (!string.Equals(ClientId, CanonicalClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Identity:WebBffClient:ClientId должен быть равен {CanonicalClientId}.");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("Identity:WebBffClient:ClientSecret должен быть задан.");
        }

        ValidateUris(RedirectUris, "Identity:WebBffClient:RedirectUris", environment);
        ValidateUris(PostLogoutRedirectUris, "Identity:WebBffClient:PostLogoutRedirectUris", environment);

        var origins = RedirectUris.Select(uri => new Uri(uri))
            .Where(uri => uri.AbsolutePath == "/signin-oidc" && uri.Query.Length == 0 && uri.UserInfo.Length == 0)
            .Select(uri => new Uri(uri.GetLeftPart(UriPartial.Authority)))
            .Distinct()
            .ToArray();
        Uri origin;
        if (string.IsNullOrEmpty(PublicOrigin) && origins.Length == 1)
        {
            origin = origins[0];
        }
        else if (Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var configured)
            && configured.AbsolutePath == "/" && configured.Query.Length == 0
            && configured.UserInfo.Length == 0 && configured.Fragment.Length == 0
            && origins.Contains(configured))
        {
            origin = configured;
        }
        else
        {
            throw new InvalidOperationException(
                "Identity:WebBffClient:PublicOrigin должен однозначно соответствовать origin зарегистрированного /signin-oidc.");
        }

        LoginContinuation = new Uri(origin, "/bff/auth/login/continue");
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
