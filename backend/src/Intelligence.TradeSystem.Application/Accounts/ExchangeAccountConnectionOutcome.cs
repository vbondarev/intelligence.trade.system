namespace Intelligence.TradeSystem.Application.Accounts;

public enum ExchangeAccountConnectionOutcome
{
    Connected = 0,
    InvalidCredentials = 1,
    PermissionsRejected = 2,
    Unavailable = 3,
    UnsupportedExchange = 4,

    /// <summary>
    /// Для того же пользователя уже существует неотключённое подключение этого provider-side аккаунта;
    /// состояние существующего подключения и его credentials не изменялись.
    /// </summary>
    AlreadyExists = 5,

    /// <summary>
    /// Ранее отключённое подключение того же provider-side аккаунта восстановлено с прежним
    /// <c>ExchangeAccountId</c> и новыми проверенными credentials.
    /// </summary>
    Reconnected = 6,
}
