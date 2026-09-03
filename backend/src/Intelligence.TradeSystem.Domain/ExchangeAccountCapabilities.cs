namespace Intelligence.TradeSystem.Domain;

/// <summary>Read-only возможности биржевого аккаунта.</summary>
[Flags]
public enum ExchangeAccountCapabilities
{
    /// <summary>Возможности не предоставлены.</summary>
    None = 0,

    /// <summary>Доступно чтение баланса.</summary>
    ReadBalance = 1,

    /// <summary>Доступно чтение позиций.</summary>
    ReadPositions = 2,
}
