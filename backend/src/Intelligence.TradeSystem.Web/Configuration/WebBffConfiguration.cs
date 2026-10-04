namespace Intelligence.TradeSystem.Web.Configuration;

/// <summary>
/// Startup-only snapshot проверенной конфигурации BFF. Ошибки конфигурации останавливают
/// запуск до приёма запросов и не содержат secret values.
/// </summary>
internal sealed class WebBffConfiguration(
    WebOidcSettings oidc,
    Uri apiBaseAddress,
    WebSessionOptions session)
{
    public WebOidcSettings Oidc { get; } = oidc;

    public Uri ApiBaseAddress { get; } = apiBaseAddress;

    public WebSessionOptions Session { get; } = session;

    public static WebBffConfiguration Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var oidcOptions = configuration.GetSection(WebOidcOptions.SectionName).Get<WebOidcOptions>()
            ?? new WebOidcOptions();
        var apiOptions = configuration.GetSection(WebApiOptions.SectionName).Get<WebApiOptions>()
            ?? new WebApiOptions();
        var sessionOptions = configuration.GetSection(WebSessionOptions.SectionName).Get<WebSessionOptions>()
            ?? new WebSessionOptions();

        var oidc = oidcOptions.Validate(environment);
        var apiBaseAddress = apiOptions.Validate(environment);
        sessionOptions.Validate();

        return new WebBffConfiguration(oidc, apiBaseAddress, sessionOptions);
    }
}
