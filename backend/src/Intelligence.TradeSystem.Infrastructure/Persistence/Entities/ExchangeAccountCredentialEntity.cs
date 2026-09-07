namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class ExchangeAccountCredentialEntity
{
    public Guid ExchangeAccountId { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] AuthenticationTag { get; set; } = [];
    public string EncryptionKeyId { get; set; } = string.Empty;
    public short FormatVersion { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
