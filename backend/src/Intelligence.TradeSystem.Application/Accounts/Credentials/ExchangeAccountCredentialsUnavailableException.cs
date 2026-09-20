namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>Сохранённые credentials существуют, но не могут безопасно использоваться.</summary>
public sealed class ExchangeAccountCredentialsUnavailableException : Exception
{
    public ExchangeAccountCredentialsUnavailableException()
        : base("The exchange account credentials are unavailable.")
    {
    }
}
