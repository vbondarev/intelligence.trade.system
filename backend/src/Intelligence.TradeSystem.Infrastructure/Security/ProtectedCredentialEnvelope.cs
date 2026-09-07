namespace Intelligence.TradeSystem.Infrastructure.Security;

internal sealed class ProtectedCredentialEnvelope
{
    public required byte[] Ciphertext { get; init; }
    public required byte[] Nonce { get; init; }
    public required byte[] AuthenticationTag { get; init; }
    public required string EncryptionKeyId { get; init; }
    public required short FormatVersion { get; init; }
}
