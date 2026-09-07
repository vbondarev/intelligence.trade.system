namespace Intelligence.TradeSystem.Infrastructure.Security;

public sealed class CredentialProtectionOptions
{
    public const string SectionName = "CredentialProtection";

    public string? ActiveKeyId { get; init; }

    public IReadOnlyDictionary<string, string> Keys { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
