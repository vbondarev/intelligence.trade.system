using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Public dimensions that identify a shared market snapshot.
/// </summary>
public readonly record struct PublicMarketSnapshotCacheKey(
    ExchangeId ExchangeId,
    string Symbol,
    MarketCategory Category)
{
    public static PublicMarketSnapshotCacheKey Create(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return new(exchangeId, symbol.Trim(), category);
    }

    public string ToStableCacheKey() => $"market-snapshot:v1:{ExchangeId}:{Category}:{Symbol}";
}
