using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Intelligence.TradeSystem.Application.Accounts.Credentials;

namespace Intelligence.TradeSystem.Infrastructure.Security;

internal static class CredentialPayloadSerializer
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Serialize(ExchangeAccountCredentialSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.Use((apiKey, apiSecret) =>
        {
            var apiKeyByteCount = Utf8.GetByteCount(apiKey);
            var apiSecretByteCount = Utf8.GetByteCount(apiSecret);
            CredentialProtectionLimits.ValidateFieldLength(apiKeyByteCount);
            CredentialProtectionLimits.ValidateFieldLength(apiSecretByteCount);

            var apiKeyBytes = new byte[apiKeyByteCount];
            var apiSecretBytes = new byte[apiSecretByteCount];
            try
            {
                Utf8.GetBytes(apiKey.AsSpan(), apiKeyBytes.AsSpan());
                Utf8.GetBytes(apiSecret.AsSpan(), apiSecretBytes.AsSpan());

                var payload = new byte[
                    CredentialProtectionLimits.LengthPrefixBytes + apiKeyBytes.Length +
                    CredentialProtectionLimits.LengthPrefixBytes + apiSecretBytes.Length];
                var offset = 0;
                BinaryPrimitives.WriteInt32BigEndian(
                    payload.AsSpan(offset, CredentialProtectionLimits.LengthPrefixBytes),
                    apiKeyBytes.Length);
                offset += CredentialProtectionLimits.LengthPrefixBytes;
                apiKeyBytes.CopyTo(payload.AsSpan(offset));
                offset += apiKeyBytes.Length;
                BinaryPrimitives.WriteInt32BigEndian(
                    payload.AsSpan(offset, CredentialProtectionLimits.LengthPrefixBytes),
                    apiSecretBytes.Length);
                offset += CredentialProtectionLimits.LengthPrefixBytes;
                apiSecretBytes.CopyTo(payload.AsSpan(offset));
                return payload;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(apiKeyBytes);
                CryptographicOperations.ZeroMemory(apiSecretBytes);
            }
        });
    }

    public static ExchangeAccountCredentialSecret Deserialize(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var apiKeyLength = ReadLength(payload, ref offset);
        var apiKey = ReadString(payload, ref offset, apiKeyLength);
        var apiSecretLength = ReadLength(payload, ref offset);
        var apiSecret = ReadString(payload, ref offset, apiSecretLength);

        if (offset != payload.Length)
        {
            throw new CredentialProtectionException("The credential payload format is invalid.");
        }

        try
        {
            return new ExchangeAccountCredentialSecret(apiKey, apiSecret);
        }
        catch (ArgumentException)
        {
            throw new CredentialProtectionException("The credential payload is invalid.");
        }
    }

    private static int ReadLength(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length - offset < CredentialProtectionLimits.LengthPrefixBytes)
        {
            throw new CredentialProtectionException("The credential payload format is invalid.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(
            payload.Slice(offset, CredentialProtectionLimits.LengthPrefixBytes));
        offset += CredentialProtectionLimits.LengthPrefixBytes;
        CredentialProtectionLimits.ValidateFieldLength(length);
        return length;
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset, int length)
    {
        if (length <= 0 || payload.Length - offset < length)
        {
            throw new CredentialProtectionException("The credential payload is invalid.");
        }

        try
        {
            var value = Utf8.GetString(payload.Slice(offset, length));
            offset += length;
            return value;
        }
        catch (DecoderFallbackException)
        {
            throw new CredentialProtectionException("The credential payload is not valid UTF-8.");
        }
    }
}
