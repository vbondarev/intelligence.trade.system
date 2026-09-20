namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

/// <summary>Разрешённая read-only capability биржевого аккаунта.</summary>
public enum ExchangeAccountCapability
{
    ReadBalance = 1,
    ReadPositions = 2,
}
