namespace Intelligence.TradeSystem.Domain;

/// <summary>Состояние подключения конкретного биржевого аккаунта.</summary>
public enum ExchangeAccountConnectionStatus
{
    /// <summary>Аккаунт ещё не проверялся или состояние неизвестно.</summary>
    Unknown = 0,

    /// <summary>Аккаунт доступен.</summary>
    Connected = 1,

    /// <summary>Аккаунт временно недоступен.</summary>
    Unavailable = 2,

    /// <summary>Аккаунт отключён.</summary>
    Disabled = 3,
}
