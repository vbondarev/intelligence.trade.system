namespace Intelligence.TradeSystem.Api.Configuration;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? MetadataAddress { get; init; }
}
