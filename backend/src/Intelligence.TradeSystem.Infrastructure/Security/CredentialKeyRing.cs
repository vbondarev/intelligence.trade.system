namespace Intelligence.TradeSystem.Infrastructure.Security;

internal sealed class CredentialKeyRing
{
    private readonly IReadOnlyDictionary<string, byte[]> keys;

    private CredentialKeyRing(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        ActiveKeyId = activeKeyId;
        this.keys = keys;
    }

    public string ActiveKeyId { get; }

    public static CredentialKeyRing Create(CredentialProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "CredentialProtection:ActiveKeyId must be configured.");
        }

        if (options.Keys is null || options.Keys.Count == 0)
        {
            throw new InvalidOperationException(
                "CredentialProtection:Keys must contain at least one key.");
        }

        var decodedKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new InvalidOperationException(
                    "CredentialProtection key ids must not be empty.");
            }

            if (pair.Key.Length > CredentialProtectionLimits.MaximumKeyIdCharacters)
            {
                throw new InvalidOperationException(
                    "CredentialProtection key ids must not exceed 128 characters.");
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(pair.Value);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    $"CredentialProtection key '{pair.Key}' is not valid Base64.",
                    exception);
            }

            if (decoded.Length != AesGcmExchangeCredentialProtector.KeySizeBytes)
            {
                throw new InvalidOperationException(
                    $"CredentialProtection key '{pair.Key}' must decode to exactly 32 bytes.");
            }

            if (!decodedKeys.TryAdd(pair.Key, decoded))
            {
                throw new InvalidOperationException(
                    $"CredentialProtection key id '{pair.Key}' is duplicated.");
            }
        }

        if (!decodedKeys.ContainsKey(options.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "CredentialProtection:ActiveKeyId must identify a configured key.");
        }

        return new CredentialKeyRing(options.ActiveKeyId, decodedKeys);
    }

    public byte[] Get(string keyId)
    {
        if (!keys.TryGetValue(keyId, out var key))
        {
            throw new CredentialProtectionException(
                "The credential encryption key is not available.");
        }

        return key;
    }
}
