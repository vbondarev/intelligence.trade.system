using System.Security.Cryptography;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Infrastructure.Security;

internal sealed class AesGcmExchangeCredentialProtector(CredentialKeyRing keyRing)
    : IExchangeCredentialProtector
{
    public const int KeySizeBytes = 32;
    private const short FormatVersion = 1;
    private const int NonceSizeBytes = 12;
    private const int AuthenticationTagSizeBytes = 16;

    public ProtectedCredentialEnvelope Protect(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret secret)
    {
        EnsureIdentity(userId, exchangeAccountId);
        ArgumentNullException.ThrowIfNull(secret);

        var encryptionKeyId = keyRing.ActiveKeyId;
        var key = keyRing.Get(encryptionKeyId);
        var payload = CredentialPayloadSerializer.Serialize(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[payload.Length];
        var authenticationTag = new byte[AuthenticationTagSizeBytes];
        var associatedData = CredentialAssociatedData.Create(
            FormatVersion,
            userId,
            exchangeAccountId,
            encryptionKeyId);
        var encryptionSucceeded = false;

        try
        {
            using var aesGcm = new AesGcm(key, AuthenticationTagSizeBytes);
            aesGcm.Encrypt(nonce, payload, ciphertext, authenticationTag, associatedData);
            encryptionSucceeded = true;
            return new ProtectedCredentialEnvelope
            {
                Ciphertext = ciphertext,
                Nonce = nonce,
                AuthenticationTag = authenticationTag,
                EncryptionKeyId = encryptionKeyId,
                FormatVersion = FormatVersion,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            if (!encryptionSucceeded)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(authenticationTag);
            }
        }
    }

    public ExchangeAccountCredentialSecret Unprotect(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ProtectedCredentialEnvelope envelope)
    {
        EnsureIdentity(userId, exchangeAccountId);
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.FormatVersion != FormatVersion)
        {
            throw new CredentialProtectionException("The credential format version is not supported.");
        }

        if (envelope.Nonce.Length != NonceSizeBytes ||
            envelope.AuthenticationTag.Length != AuthenticationTagSizeBytes ||
            envelope.Ciphertext.Length == 0)
        {
            throw new CredentialProtectionException("The credential protection envelope is invalid.");
        }

        var key = keyRing.Get(envelope.EncryptionKeyId);
        var associatedData = CredentialAssociatedData.Create(
            envelope.FormatVersion,
            userId,
            exchangeAccountId,
            envelope.EncryptionKeyId);
        var plaintext = new byte[envelope.Ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, AuthenticationTagSizeBytes);
            aesGcm.Decrypt(
                envelope.Nonce,
                envelope.Ciphertext,
                envelope.AuthenticationTag,
                plaintext,
                associatedData);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CredentialProtectionException(
                "The credential protection authentication failed.");
        }

        try
        {
            return CredentialPayloadSerializer.Deserialize(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void EnsureIdentity(UserId userId, ExchangeAccountId exchangeAccountId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));

        if (exchangeAccountId == default)
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.",
                nameof(exchangeAccountId));
    }
}
