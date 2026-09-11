namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Указывает, что необходимые вышестоящие рыночные данные временно недоступны,
/// поэтому рыночный снимок нельзя корректно построить.
/// </summary>
public sealed class MarketDataUnavailableException : Exception
{
    public MarketDataUnavailableException(string message)
        : base(message)
    {
    }

    public MarketDataUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
