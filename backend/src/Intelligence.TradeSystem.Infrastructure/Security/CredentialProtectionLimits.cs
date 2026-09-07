namespace Intelligence.TradeSystem.Infrastructure.Security;

internal static class CredentialProtectionLimits
{
    public const int LengthPrefixBytes = sizeof(int);
    public const int MaximumFieldBytes = 1024 * 1024;
    public const int MaximumPayloadBytes =
        LengthPrefixBytes + MaximumFieldBytes +
        LengthPrefixBytes + MaximumFieldBytes;
    public const int MaximumKeyIdCharacters = 128;

    public static void ValidateFieldLength(int length)
    {
        if (length <= 0 || length > MaximumFieldBytes)
        {
            throw new CredentialProtectionException("The credential payload field length is invalid.");
        }
    }

    public static void ValidateKeyId(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > MaximumKeyIdCharacters)
        {
            throw new CredentialProtectionException("The credential protection key id is invalid.");
        }
    }
}
