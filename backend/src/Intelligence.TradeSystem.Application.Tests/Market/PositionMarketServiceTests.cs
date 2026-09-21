using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Market;

public sealed class PositionMarketServiceTests
{
    [Fact]
    public async Task Missing_identity_returns_not_found_without_market_io()
    {
        var userId = UserId.New();
        var positionId = PositionId.New();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(userId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionMarketIdentity?)null);
        var service = new PositionMarketService(
            identityStore.Object,
            snapshotService.Object,
            marketDataProvider.Object);

        var market = await service.GetMarketAsync(userId, positionId);
        var candles = await service.GetCandlesAsync(
            userId,
            positionId,
            KlineInterval.FifteenMinutes,
            200);

        Assert.Null(market);
        Assert.Null(candles);
        snapshotService.VerifyNoOtherCalls();
        marketDataProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Market_uses_persisted_identity_and_snapshot_service()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var snapshot = CreateSnapshot();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(userId, identity.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        snapshotService
            .Setup(service => service.BuildSnapshotAsync(
                identity.ExchangeId,
                identity.Symbol,
                identity.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        var service = new PositionMarketService(
            identityStore.Object,
            snapshotService.Object,
            marketDataProvider.Object);

        var result = await service.GetMarketAsync(userId, identity.PositionId);

        Assert.NotNull(result);
        Assert.Same(snapshot, result!.Snapshot);
        Assert.Equal(identity, result.Identity);
        snapshotService.VerifyAll();
        marketDataProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Candles_use_persisted_identity_and_are_sorted_oldest_first()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var first = CreateKline(identity.Symbol, identity.MarketCategory, KlineInterval.OneHour, 1);
        var second = CreateKline(identity.Symbol, identity.MarketCategory, KlineInterval.OneHour, 2);
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(userId, identity.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                KlineInterval.OneHour,
                null,
                null,
                200,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([second, first]);
        var service = new PositionMarketService(
            identityStore.Object,
            snapshotService.Object,
            marketDataProvider.Object);

        var result = await service.GetCandlesAsync(
            userId,
            identity.PositionId,
            KlineInterval.OneHour,
            200);

        Assert.NotNull(result);
        Assert.Equal([first, second], result!.Items);
        snapshotService.VerifyNoOtherCalls();
        marketDataProvider.VerifyAll();
    }

    [Fact]
    public async Task Empty_candles_are_reported_as_market_data_unavailable()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(userId, identity.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                KlineInterval.FifteenMinutes,
                null,
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new PositionMarketService(
            identityStore.Object,
            snapshotService.Object,
            marketDataProvider.Object);

        await Assert.ThrowsAsync<MarketDataUnavailableException>(
            () => service.GetCandlesAsync(
                userId,
                identity.PositionId,
                KlineInterval.FifteenMinutes,
                1));

        snapshotService.VerifyNoOtherCalls();
        marketDataProvider.VerifyAll();
    }

    [Fact]
    public async Task Unsupported_exchange_fails_before_external_market_call()
    {
        var userId = UserId.New();
        var identity = CreateIdentity() with { ExchangeId = (ExchangeId)999 };
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(userId, identity.PositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        var service = new PositionMarketService(
            identityStore.Object,
            snapshotService.Object,
            marketDataProvider.Object);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.GetCandlesAsync(
                userId,
                identity.PositionId,
                KlineInterval.OneHour,
                200));

        snapshotService.VerifyNoOtherCalls();
        marketDataProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_identity_and_market_provider()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        using var cancellation = new CancellationTokenSource();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(userId, identity.PositionId, cancellation.Token))
            .ReturnsAsync(identity);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                KlineInterval.OneHour,
                null,
                null,
                10,
                cancellation.Token))
            .ReturnsAsync([CreateKline(identity.Symbol, identity.MarketCategory, KlineInterval.OneHour, 1)]);
        var service = new PositionMarketService(
            identityStore.Object,
            snapshotService.Object,
            marketDataProvider.Object);

        await service.GetCandlesAsync(
            userId,
            identity.PositionId,
            KlineInterval.OneHour,
            10,
            cancellation.Token);

        identityStore.VerifyAll();
        marketDataProvider.VerifyAll();
    }

    private static PositionMarketIdentity CreateIdentity() => new(
        PositionId.New(),
        ExchangeAccountId.New(),
        ExchangeId.Bybit,
        "BTCUSDT",
        MarketCategory.Linear);

    private static Kline CreateKline(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        int minute) =>
        new(
            symbol,
            category,
            interval,
            new DateTime(2026, 9, 20, 10, minute, 0, DateTimeKind.Unspecified),
            100m + minute,
            110m + minute,
            90m + minute,
            105m + minute,
            10m,
            1_000m);

    private static MarketSnapshot CreateSnapshot() => new()
    {
        Exchange = "Bybit",
        Symbol = "BTCUSDT",
        Category = "linear",
        CapturedAtUtc = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
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

    private static TimeframeAnalysisSnapshot CreateTimeframe(string timeframe) => new()
    {
        Timeframe = timeframe,
        LastCandleOpenTimeUtc = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
        LastCandle = new(),
    };
}
