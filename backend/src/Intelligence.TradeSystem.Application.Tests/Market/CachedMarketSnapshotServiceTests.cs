using System.Reflection;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Tests.Market;

public sealed class CachedMarketSnapshotServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Invalid_Symbols(string? symbol)
    {
        var action = () => PublicMarketSnapshotCacheKey.Create(
            ExchangeId.Bybit,
            symbol!,
            MarketCategory.Linear);

        Assert.ThrowsAny<ArgumentException>(action);
    }

    [Fact]
    public void Key_Has_No_Public_Constructor()
    {
        Assert.Empty(typeof(PublicMarketSnapshotCacheKey)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

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
        var service = new CachedMarketSnapshotService(cache);

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
        public PublicMarketSnapshotCacheKey Key { get; private set; } = null!;

        public int CallCount { get; private set; }

        public ValueTask<MarketSnapshot> GetOrCreateAsync(
            PublicMarketSnapshotCacheKey key,
            CancellationToken cancellationToken = default)
        {
            Key = key;
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }

}
