using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class ExchangeAccountMapper
{
    public static ExchangeAccountEntity ToEntity(ExchangeAccount account) => new()
    {
        Id = account.Id.Value,
        UserId = account.UserId.Value,
        ExchangeId = account.ExchangeId,
        ConnectionStatus = account.ConnectionStatus,
        Capabilities = account.Capabilities,
        LastSyncedAt = PersistenceDateTime.ToUtc(account.LastSyncedAt),
        LastError = account.LastError,
    };

    public static ExchangeAccount ToDomain(ExchangeAccountEntity entity) =>
        ExchangeAccount.Create(
            ExchangeAccountId.FromGuid(entity.Id),
            UserId.FromGuid(entity.UserId),
            entity.ExchangeId,
            entity.ConnectionStatus,
            entity.Capabilities,
            PersistenceDateTime.ToUtc(entity.LastSyncedAt),
            entity.LastError);
}
