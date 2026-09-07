using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Intelligence.TradeSystem.Application.Accounts.Credentials;

namespace Intelligence.TradeSystem.Infrastructure.Security;

internal static class CredentialPayloadSerializer
{
    private const int LengthPrefixBytes = sizeof(int);
    private const int MaximumFieldBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Serialize(ExchangeAccountCredentialSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.Use((apiKey, apiSecret) =>
        {
            var apiKeyBytes = Utf8.GetBytes(apiKey);
            var apiSecretBytes = Utf8.GetBytes(apiSecret);
            try
            {
                ValidateFieldLength(apiKeyBytes.Length);
                ValidateFieldLength(apiSecretBytes.Length);

                var payload = new byte[
                    LengthPrefixBytes + apiKeyBytes.Length +
                    LengthPrefixBytes + apiSecretBytes.Length];
                var offset = 0;
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, LengthPrefixBytes), apiKeyBytes.Length);
                offset += LengthPrefixBytes;
                apiKeyBytes.CopyTo(payload.AsSpan(offset));
                offset += apiKeyBytes.Length;
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, LengthPrefixBytes), apiSecretBytes.Length);
                offset += LengthPrefixBytes;
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
        if (payload.Length - offset < LengthPrefixBytes)
        {
            throw new CredentialProtectionException("The credential payload format is invalid.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(offset, LengthPrefixBytes));
        offset += LengthPrefixBytes;
        ValidateFieldLength(length);
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

    private static void ValidateFieldLength(int length)
    {
        if (length <= 0 || length > MaximumFieldBytes)
        {
            throw new CredentialProtectionException("The credential payload field length is invalid.");
        }
    }
}
