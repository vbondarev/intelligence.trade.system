using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Public dimensions that identify a shared market snapshot.
/// </summary>
public sealed record PublicMarketSnapshotCacheKey
{
    private PublicMarketSnapshotCacheKey(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category)
    {
        ExchangeId = exchangeId;
        Symbol = symbol;
        Category = category;
    }

    public ExchangeId ExchangeId { get; }

    public string Symbol { get; }

    public MarketCategory Category { get; }

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
