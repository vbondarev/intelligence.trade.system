namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Параметры confidential OIDC client, которым BFF представляется в Identity.
/// </summary>
public sealed class BffOidcOptions
{
    public const string SectionName = "Bff:Oidc";

    /// <summary>
    /// Публичный issuer Identity, который видит browser и который проверяется в id_token.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>
    /// Адрес discovery document; по умолчанию вычисляется из <see cref="Authority"/>.
    /// </summary>
    public string? MetadataAddress { get; init; }

    /// <summary>
    /// Внутренний адрес Identity для server-to-server запросов; по умолчанию authority
    /// из <see cref="MetadataAddress"/>.
    /// </summary>
    public string? BackchannelBaseAddress { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    internal BffOidcSettings Validate(IHostEnvironment environment)
    {
        var authority = BffConfigurationValidation.RequireAbsoluteUri(
            Authority,
            "Bff:Oidc:Authority",
            environment);
        var metadataAddress = BffConfigurationValidation.RequireAbsoluteUri(
            string.IsNullOrWhiteSpace(MetadataAddress)
                ? new Uri(authority, ".well-known/openid-configuration").AbsoluteUri
                : MetadataAddress,
            "Bff:Oidc:MetadataAddress",
            environment);
        var backchannelBaseAddress = BffConfigurationValidation.RequireAbsoluteUri(
            string.IsNullOrWhiteSpace(BackchannelBaseAddress)
                ? metadataAddress.GetLeftPart(UriPartial.Authority)
                : BackchannelBaseAddress,
            "Bff:Oidc:BackchannelBaseAddress",
            environment);

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Bff:Oidc:ClientId должен быть задан.");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("Bff:Oidc:ClientSecret должен быть задан.");
        }

        return new BffOidcSettings(
            authority,
            metadataAddress,
            backchannelBaseAddress,
            ClientId,
            ClientSecret);
    }
}

/// <summary>
/// Проверенные OIDC-параметры BFF. Тип намеренно не является record, чтобы
/// автоматический <c>ToString</c> не раскрывал client secret.
/// </summary>
internal sealed class BffOidcSettings(
    Uri authority,
    Uri metadataAddress,
    Uri backchannelBaseAddress,
    string clientId,
    string clientSecret)
{
    public Uri Authority { get; } = authority;

    public Uri MetadataAddress { get; } = metadataAddress;

    public Uri BackchannelBaseAddress { get; } = backchannelBaseAddress;

    public string ClientId { get; } = clientId;

    public string ClientSecret { get; } = clientSecret;
}
