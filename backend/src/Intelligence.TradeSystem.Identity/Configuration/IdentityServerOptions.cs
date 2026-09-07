namespace Intelligence.TradeSystem.Identity.Configuration;

public sealed class IdentityServerOptions
{
    public const string SectionName = "Identity";

    public string? Issuer { get; init; }
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxFailedAccessAttempts { get; init; } = 5;
    public TimeSpan DefaultLockoutTimeSpan { get; init; } = TimeSpan.FromMinutes(5);
    public bool AllowedForNewUsers { get; init; } = true;
    public List<CertificateOptions> SigningCertificates { get; init; } = [];
    public string? EncryptionCertificatePath { get; init; }
    public string? EncryptionCertificatePassword { get; init; }

    public void Validate()
    {
        if (AccessTokenLifetime <= TimeSpan.Zero || AccessTokenLifetime > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException(
                "Identity:AccessTokenLifetime must be greater than zero and no longer than one hour.");
        }

        if (MaxFailedAccessAttempts is < 3 or > 10)
        {
            throw new InvalidOperationException(
                "Identity:MaxFailedAccessAttempts must be between 3 and 10.");
        }

        if (DefaultLockoutTimeSpan <= TimeSpan.Zero || DefaultLockoutTimeSpan > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException(
                "Identity:DefaultLockoutTimeSpan must be greater than zero and no longer than one hour.");
        }
    }
}
