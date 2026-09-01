namespace Intelligence.TradeSystem.MarketIntelligence.Analysis;

/// <summary>
/// Исключение, сигнализирующее о проблеме качества данных от внешнего провайдера,
/// а не о некорректном вводе клиента. Маппится в HTTP 5xx (503 Service Unavailable).
/// </summary>
public sealed class DataSourceException : InvalidOperationException
{
    public DataSourceException(string message)
        : base(message)
    {
    }

    public DataSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
