namespace Intelligence.TradeSystem.Identity.Configuration;

public sealed class IdentityServerOptions
{
    public const string SectionName = "Identity";

    public string? Issuer { get; init; }
    public string? SigningCertificatePath { get; init; }
    public string? SigningCertificatePassword { get; init; }
    public string? EncryptionCertificatePath { get; init; }
    public string? EncryptionCertificatePassword { get; init; }
}
