namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Startup-only snapshot проверенной конфигурации BFF. Ошибки конфигурации останавливают
/// запуск до приёма запросов и не содержат secret values.
/// </summary>
internal sealed class BffConfiguration(
    BffOidcSettings oidc,
    Uri apiBaseAddress,
    BffSessionOptions session)
{
    public BffOidcSettings Oidc { get; } = oidc;

    public Uri ApiBaseAddress { get; } = apiBaseAddress;

    public BffSessionOptions Session { get; } = session;

    public static BffConfiguration Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var oidcOptions = configuration.GetSection(BffOidcOptions.SectionName).Get<BffOidcOptions>()
            ?? new BffOidcOptions();
        var apiOptions = configuration.GetSection(BffApiOptions.SectionName).Get<BffApiOptions>()
            ?? new BffApiOptions();
        var sessionOptions = configuration.GetSection(BffSessionOptions.SectionName).Get<BffSessionOptions>()
            ?? new BffSessionOptions();

        var oidc = oidcOptions.Validate(environment);
        var apiBaseAddress = apiOptions.Validate(environment);
        sessionOptions.Validate();

        return new BffConfiguration(oidc, apiBaseAddress, sessionOptions);
    }
}
