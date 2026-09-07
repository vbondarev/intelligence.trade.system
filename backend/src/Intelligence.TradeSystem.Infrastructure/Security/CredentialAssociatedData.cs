using System.Buffers.Binary;
using System.Text;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Infrastructure.Security;

internal static class CredentialAssociatedData
{
    private const string Purpose = "Intelligence.TradeSystem.ExchangeAccountCredentials";

    public static byte[] Create(
        short formatVersion,
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        string encryptionKeyId)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(Purpose);
        var keyIdBytes = Encoding.UTF8.GetBytes(encryptionKeyId);
        var data = new byte[
            sizeof(int) + purposeBytes.Length +
            sizeof(short) +
            sizeof(long) + sizeof(long) +
            sizeof(long) + sizeof(long) +
            sizeof(int) + keyIdBytes.Length];

        var offset = 0;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, sizeof(int)), purposeBytes.Length);
        offset += sizeof(int);
        purposeBytes.CopyTo(data.AsSpan(offset));
        offset += purposeBytes.Length;
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(offset, sizeof(short)), formatVersion);
        offset += sizeof(short);
        userId.Value.TryWriteBytes(data.AsSpan(offset, sizeof(long) + sizeof(long)));
        offset += sizeof(long) + sizeof(long);
        exchangeAccountId.Value.TryWriteBytes(data.AsSpan(offset, sizeof(long) + sizeof(long)));
        offset += sizeof(long) + sizeof(long);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, sizeof(int)), keyIdBytes.Length);
        offset += sizeof(int);
        keyIdBytes.CopyTo(data.AsSpan(offset));
        return data;
    }
}
