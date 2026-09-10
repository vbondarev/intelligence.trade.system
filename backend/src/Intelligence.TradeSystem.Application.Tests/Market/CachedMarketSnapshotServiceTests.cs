using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Tests.Market;

public sealed class CachedMarketSnapshotServiceTests
{
    [Fact]
    public async Task BuildSnapshotAsync_Normalizes_The_Symbol_Only_For_The_Public_Cache_Key()
    {
        var expected = new MarketSnapshot
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "linear",
            CapturedAtUtc = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero),
            Price = new(),
            Derivatives = new(),
            OrderBook = new(),
            TradeFlow = new(),
            M15 = new()
            {
                Timeframe = "15m",
                LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
                LastCandle = new(),
            },
            H1 = new()
            {
                Timeframe = "1h",
                LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
                LastCandle = new(),
            },
            H4 = new()
            {
                Timeframe = "4h",
                LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
                LastCandle = new(),
            },
            D1 = new()
            {
                Timeframe = "1d",
                LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
                LastCandle = new(),
            },
            Sentiment = new(),
        };
        var cache = new RecordingCache(expected);
        var service = new CachedMarketSnapshotService(
            new MarketSnapshotService(new ThrowingCollector()),
            cache);

        var result = await service.BuildSnapshotAsync(
            ExchangeId.Bybit,
            " BTCUSDT ",
            MarketCategory.Linear);

        Assert.Same(expected, result);
        Assert.Equal(
            PublicMarketSnapshotCacheKey.Create(
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Linear),
            cache.Key);
        Assert.Equal(
            "market-snapshot:v1:Bybit:Linear:BTCUSDT",
            cache.Key.ToStableCacheKey());
        Assert.Equal(1, cache.CallCount);
    }

    private sealed class RecordingCache(MarketSnapshot result) : IPublicMarketSnapshotCache
    {
        public PublicMarketSnapshotCacheKey Key { get; private set; }

        public int CallCount { get; private set; }

        public ValueTask<MarketSnapshot> GetOrCreateAsync(
            PublicMarketSnapshotCacheKey key,
            Func<CancellationToken, ValueTask<MarketSnapshot>> factory,
            CancellationToken cancellationToken = default)
        {
            Key = key;
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingCollector : IPublicMarketDataCollector
    {
        public Task<CollectedPublicMarketData> CollectAsync(
            ExchangeId exchangeId,
            string symbol,
            MarketCategory category,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cache should return before invoking the builder.");
    }
}
