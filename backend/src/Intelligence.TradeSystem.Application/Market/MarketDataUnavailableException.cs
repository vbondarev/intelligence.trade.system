namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Indicates that required upstream market data is temporarily unavailable,
/// so a market snapshot cannot be built correctly.
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
