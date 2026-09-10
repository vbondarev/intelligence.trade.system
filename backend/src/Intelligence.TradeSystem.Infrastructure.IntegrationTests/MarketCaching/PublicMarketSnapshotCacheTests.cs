using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Infrastructure.MarketCaching;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests.MarketCaching;

public sealed class PublicMarketSnapshotCacheTests
{
    [Fact]
    public async Task Same_Key_Concurrent_Callers_Use_One_Source_Build()
    {
        using var fixture = CreateCache(TimeSpan.FromSeconds(1));
        var key = PublicMarketSnapshotCacheKey.Create(
            ExchangeId.Bybit,
            "BTCUSDT",
            MarketCategory.Linear);
        var buildStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var buildCount = 0;

        ValueTask<MarketSnapshot> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref buildCount);
            buildStarted.TrySetResult(true);
            return WaitForReleaseAsync(cancellationToken);
        }

        async ValueTask<MarketSnapshot> WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            await releaseBuild.Task.WaitAsync(cancellationToken);
            return CreateSnapshot("BTCUSDT", DateTimeOffset.UtcNow);
        }

        var callers = Enumerable.Range(0, 20)
            .Select(_ => fixture.Cache.GetOrCreateAsync(key, Factory).AsTask())
            .ToArray();

        await buildStarted.Task;
        releaseBuild.SetResult(true);

        var results = await Task.WhenAll(callers);

        Assert.Equal(1, buildCount);
        Assert.Equal(20, results.Length);
        Assert.All(results, result => Assert.Equal("BTCUSDT", result.Symbol));
    }

    [Fact]
    public async Task Different_Keys_Build_In_Parallel()
    {
        using var fixture = CreateCache(TimeSpan.FromSeconds(1));
        var releaseBuilds = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bothBuildsStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activeBuilds = 0;
        var maxActiveBuilds = 0;
        var startedBuilds = 0;

        async ValueTask<MarketSnapshot> Factory(
            string symbol,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref activeBuilds);
            UpdateMaximum(ref maxActiveBuilds, active);
            if (Interlocked.Increment(ref startedBuilds) == 2)
            {
                bothBuildsStarted.TrySetResult(true);
            }

            try
            {
                await releaseBuilds.Task.WaitAsync(cancellationToken);
                return CreateSnapshot(symbol, DateTimeOffset.UtcNow);
            }
            finally
            {
                Interlocked.Decrement(ref activeBuilds);
            }
        }

        var first = fixture.Cache.GetOrCreateAsync(
            PublicMarketSnapshotCacheKey.Create(
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Linear),
            cancellationToken => Factory("BTCUSDT", cancellationToken)).AsTask();
        var second = fixture.Cache.GetOrCreateAsync(
            PublicMarketSnapshotCacheKey.Create(
                ExchangeId.Bybit,
                "ETHUSDT",
                MarketCategory.Linear),
            cancellationToken => Factory("ETHUSDT", cancellationToken)).AsTask();

        try
        {
            await bothBuildsStarted.Task;
            Assert.True(maxActiveBuilds >= 2);
        }
        finally
        {
            releaseBuilds.TrySetResult(true);
        }

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Different_Categories_Use_Different_Entries()
    {
        using var fixture = CreateCache(TimeSpan.FromSeconds(1));
        var builds = 0;

        ValueTask<MarketSnapshot> Factory(CancellationToken _) =>
            ValueTask.FromResult(CreateSnapshot(
                "BTCUSDT",
                DateTimeOffset.UtcNow));

        var linear = await fixture.Cache.GetOrCreateAsync(
            PublicMarketSnapshotCacheKey.Create(
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Linear),
            _ =>
            {
                Interlocked.Increment(ref builds);
                return Factory(default);
            });
        var spot = await fixture.Cache.GetOrCreateAsync(
            PublicMarketSnapshotCacheKey.Create(
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Spot),
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot(
                    "BTCUSDT",
                    DateTimeOffset.UtcNow));
            });

        Assert.Equal(2, builds);
        Assert.NotSame(linear, spot);
    }

    [Fact]
    public async Task Failed_Build_Is_Not_Cached()
    {
        using var fixture = CreateCache(TimeSpan.FromSeconds(1));
        var key = PublicMarketSnapshotCacheKey.Create(
            ExchangeId.Bybit,
            "BTCUSDT",
            MarketCategory.Linear);
        var builds = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Cache.GetOrCreateAsync(
                key,
                _ =>
                {
                    Interlocked.Increment(ref builds);
                    return ValueTask.FromException<MarketSnapshot>(
                        new InvalidOperationException("Injected source failure."));
                }).AsTask());

        var result = await fixture.Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot(
                    "BTCUSDT",
                    DateTimeOffset.UtcNow));
            });

        Assert.Equal(2, builds);
        Assert.Equal("BTCUSDT", result.Symbol);
    }

    [Fact]
    public async Task Cancelling_One_Waiter_Does_Not_Cancel_The_Shared_Build()
    {
        using var fixture = CreateCache(TimeSpan.FromSeconds(1));
        var key = PublicMarketSnapshotCacheKey.Create(
            ExchangeId.Bybit,
            "BTCUSDT",
            MarketCategory.Linear);
        var buildStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstCancellation = new CancellationTokenSource();
        var factoryCalls = 0;

        async ValueTask<MarketSnapshot> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            buildStarted.TrySetResult(true);
            await releaseBuild.Task.WaitAsync(cancellationToken);
            Assert.False(cancellationToken.IsCancellationRequested);
            return CreateSnapshot("BTCUSDT", DateTimeOffset.UtcNow);
        }

        var first = fixture.Cache.GetOrCreateAsync(
            key,
            Factory,
            firstCancellation.Token).AsTask();
        await buildStarted.Task;
        var second = fixture.Cache.GetOrCreateAsync(key, Factory).AsTask();

        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        releaseBuild.SetResult(true);

        var result = await second;

        Assert.Equal(1, factoryCalls);
        Assert.Equal("BTCUSDT", result.Symbol);
    }

    [Fact]
    public async Task Expiration_Rebuilds_Without_Changing_The_Cached_Capture_Time()
    {
        using var fixture = CreateCache(TimeSpan.FromMilliseconds(200));
        var key = PublicMarketSnapshotCacheKey.Create(
            ExchangeId.Bybit,
            "BTCUSDT",
            MarketCategory.Linear);
        var firstCapture = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var builds = 0;

        var first = await fixture.Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot("BTCUSDT", firstCapture));
            });
        var hit = await fixture.Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot(
                    "BTCUSDT",
                    firstCapture.AddMinutes(1)));
            });

        await Task.Delay(350);

        var expired = await fixture.Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot(
                    "BTCUSDT",
                    firstCapture.AddMinutes(1)));
            });

        Assert.Equal(first.Symbol, hit.Symbol);
        Assert.Equal(first.Category, hit.Category);
        Assert.Equal(firstCapture, hit.CapturedAtUtc);
        Assert.NotSame(first, expired);
        Assert.Equal(firstCapture.AddMinutes(1), expired.CapturedAtUtc);
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task Disabled_Cache_Always_Invokes_The_Source()
    {
        using var fixture = CreateCache(TimeSpan.FromSeconds(1), enabled: false);
        var key = PublicMarketSnapshotCacheKey.Create(
            ExchangeId.Bybit,
            "BTCUSDT",
            MarketCategory.Linear);
        var builds = 0;

        await fixture.Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot("BTCUSDT", DateTimeOffset.UtcNow));
            });
        await fixture.Cache.GetOrCreateAsync(
            key,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return ValueTask.FromResult(CreateSnapshot("BTCUSDT", DateTimeOffset.UtcNow));
            });

        Assert.Equal(2, builds);
    }

    [Fact]
    public void Invalid_EntryLifetime_Is_Rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PublicMarketSnapshotCacheOptions.SectionName}:EntryLifetime"] =
                    "00:10:00",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPublicMarketSnapshotCaching(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider
                .GetRequiredService<IOptions<PublicMarketSnapshotCacheOptions>>()
                .Value);
    }

    [Fact]
    public async Task Cache_Is_Shared_Between_Two_DI_Scopes()
    {
        var collector = new BlockingCollector();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PublicMarketSnapshotCacheOptions.SectionName}:Enabled"] = "true",
                [$"{PublicMarketSnapshotCacheOptions.SectionName}:EntryLifetime"] =
                    "00:00:01",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddApplication();
        services.RemoveAll<IPublicMarketDataCollector>();
        services.AddSingleton<IPublicMarketDataCollector>(collector);
        services.AddPublicMarketSnapshotCaching(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstTask = firstScope.ServiceProvider
            .GetRequiredService<IMarketSnapshotService>()
            .BuildSnapshotAsync(
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Linear);
        await collector.CollectionStarted.Task;
        var secondTask = secondScope.ServiceProvider
            .GetRequiredService<IMarketSnapshotService>()
            .BuildSnapshotAsync(
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Linear);

        collector.ReleaseCollection.TrySetResult(true);

        var snapshots = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, collector.CallCount);
        Assert.Equal(snapshots[0].Exchange, snapshots[1].Exchange);
        Assert.Equal(snapshots[0].Symbol, snapshots[1].Symbol);
        Assert.Equal(snapshots[0].Category, snapshots[1].Category);
        Assert.Equal(snapshots[0].CapturedAtUtc, snapshots[1].CapturedAtUtc);
    }

    private static CacheFixture CreateCache(
        TimeSpan entryLifetime,
        bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        var provider = services.BuildServiceProvider();
        var cache = new PublicMarketSnapshotCache(
            provider.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
            Options.Create(new PublicMarketSnapshotCacheOptions
            {
                Enabled = enabled,
                EntryLifetime = entryLifetime,
            }));
        return new CacheFixture(provider, cache);
    }

    private static MarketSnapshot CreateSnapshot(
        string symbol,
        DateTimeOffset capturedAtUtc) =>
        new()
        {
            Exchange = "Bybit",
            Symbol = symbol,
            Category = "linear",
            CapturedAtUtc = capturedAtUtc,
            Price = new(),
            Derivatives = new(),
            OrderBook = new(),
            TradeFlow = new(),
            M15 = CreateTimeframe("15m"),
            H1 = CreateTimeframe("1h"),
            H4 = CreateTimeframe("4h"),
            D1 = CreateTimeframe("1d"),
            Sentiment = new(),
        };

    private static TimeframeAnalysisSnapshot CreateTimeframe(string timeframe) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(
                2026,
                9,
                10,
                10,
                0,
                0,
                TimeSpan.Zero),
            LastCandle = new(),
        };

    private static void UpdateMaximum(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current
                || Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }

    private sealed record CacheFixture(
        ServiceProvider Provider,
        PublicMarketSnapshotCache Cache) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class BlockingCollector : IPublicMarketDataCollector
    {
        public TaskCompletionSource<bool> CollectionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseCollection { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _callCount);

        private int _callCount;

        public async Task<CollectedPublicMarketData> CollectAsync(
            ExchangeId exchangeId,
            string symbol,
            MarketCategory category,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            CollectionStarted.TrySetResult(true);
            await ReleaseCollection.Task.WaitAsync(cancellationToken);
            return CreateCollectedData(exchangeId, symbol, category);
        }
    }

    private static CollectedPublicMarketData CreateCollectedData(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category) =>
        new()
        {
            ExchangeId = exchangeId,
            Symbol = symbol,
            Category = category,
            Ticker = new Ticker(
                symbol,
                category,
                100m,
                101m,
                99m,
                99.5m,
                10m,
                100.5m,
                12m,
                0.015m,
                110m,
                90m,
                1_500_000m,
                150_000_000m)
            {
                FundingRate = 0.0004m,
                NextFundingTimeUtc = new DateTimeOffset(
                    2026,
                    9,
                    10,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                OpenInterest = 2_000m,
                OpenInterestValue = 200_000m,
            },
            OrderBook = new OrderBook(
                symbol,
                category,
                new DateTimeOffset(
                    2026,
                    9,
                    10,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                [new OrderBookEntry(99.5m, 10m)],
                [new OrderBookEntry(100.5m, 12m)]),
            Trades =
            [
                new Trade(
                    symbol,
                    category,
                    new DateTimeOffset(2026, 9, 10, 11, 55, 0, TimeSpan.Zero),
                    TradeSide.Buy,
                    8m,
                    100m),
            ],
            M15Klines = CreateKlines(symbol, category, KlineInterval.FifteenMinutes),
            H1Klines = CreateKlines(symbol, category, KlineInterval.OneHour),
            H4Klines = CreateKlines(symbol, category, KlineInterval.FourHours),
            D1Klines = CreateKlines(symbol, category, KlineInterval.OneDay),
        };

    private static Kline[] CreateKlines(
        string symbol,
        MarketCategory category,
        KlineInterval interval)
    {
        var step = interval switch
        {
            KlineInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            KlineInterval.OneHour => TimeSpan.FromHours(1),
            KlineInterval.FourHours => TimeSpan.FromHours(4),
            KlineInterval.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1),
        };
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        return Enumerable.Range(0, 30)
            .Select(index =>
            {
                var open = 100m + index;
                var close = open + 0.75m;
                return new Kline(
                    symbol,
                    category,
                    interval,
                    start.Add(step * index),
                    open,
                    close + 0.5m,
                    open - 0.5m,
                    close,
                    1_000m + (index * 25m),
                    100_000m + (index * 1_000m));
            })
            .ToArray();
    }
}
