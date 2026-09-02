namespace Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

/// <summary>
/// Transient infrastructure input for an authenticated Bybit client.
/// </summary>
public sealed class BybitCredentials
{
    internal string ApiKey { get; }
    internal string ApiSecret { get; }

    public BybitCredentials(string apiKey, string apiSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiSecret);

        ApiKey = apiKey;
        ApiSecret = apiSecret;
    }
}
