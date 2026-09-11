namespace Intelligence.TradeSystem.Infrastructure.Security;

/// <summary>
/// Контролируемая ошибка для недействительных, недоступных или подменённых данных защиты учётных данных.
/// </summary>
public sealed class CredentialProtectionException : Exception
{
    public CredentialProtectionException(string message)
        : base(message)
    {
    }
}
