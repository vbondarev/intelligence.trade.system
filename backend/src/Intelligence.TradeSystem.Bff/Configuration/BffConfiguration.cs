using System.Net;

namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Startup-only snapshot проверенной конфигурации BFF. Ошибки конфигурации останавливают
/// запуск до приёма запросов и не содержат secret values.
/// </summary>
internal sealed class BffConfiguration(
    BffOidcSettings oidc,
    Uri apiBaseAddress,
    BffSessionOptions session,
    IReadOnlyList<IPNetwork> trustedProxyNetworks)
{
    public BffOidcSettings Oidc { get; } = oidc;

    public Uri ApiBaseAddress { get; } = apiBaseAddress;

    public BffSessionOptions Session { get; } = session;

    public IReadOnlyList<IPNetwork> TrustedProxyNetworks { get; } = trustedProxyNetworks;

    public static BffConfiguration Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var oidcOptions = configuration.GetSection(BffOidcOptions.SectionName).Get<BffOidcOptions>()
            ?? new BffOidcOptions();
        var apiOptions = configuration.GetSection(BffApiOptions.SectionName).Get<BffApiOptions>()
            ?? new BffApiOptions();
        var sessionOptions = configuration.GetSection(BffSessionOptions.SectionName).Get<BffSessionOptions>()
            ?? new BffSessionOptions();
        var forwardedHeadersOptions = configuration
            .GetSection(BffForwardedHeadersOptions.SectionName)
            .Get<BffForwardedHeadersOptions>()
            ?? new BffForwardedHeadersOptions();

        var oidc = oidcOptions.Validate(environment);
        var apiBaseAddress = apiOptions.Validate(environment);
        sessionOptions.Validate();
        var trustedProxyNetworks = forwardedHeadersOptions.Validate();

        return new BffConfiguration(oidc, apiBaseAddress, sessionOptions, trustedProxyNetworks);
    }
}
