using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class MarketMarkPriceServiceTests
{
    [Fact]
    public async Task Reads_each_shared_instrument_once_and_projects_per_position_settlement_asset()
    {
        var symbol = InstrumentId.From("BTCUSDT");
        var observedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var positions = new[]
        {
            CreatePosition("USDT", size: 0.5m),
            CreatePosition("USDC", size: 0.25m),
        };
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.Is<IReadOnlyCollection<InstrumentId>>(items =>
                    items.Count == 1 && items.Contains(symbol)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MarketMarkPrice
                {
                    Exchange = ExchangeId.Bybit,
                    Category = MarketCategory.Linear,
                    Symbol = symbol,
                    MarkPrice = 40_000m,
                    ObservedAt = observedAt,
                    ReceivedAt = observedAt,
                    Source = MarketMarkPriceSource.PublicTicker,
                },
            ]);
        var service = new MarketMarkPriceService(store.Object, new MutableTimeProvider(observedAt.AddSeconds(1)));

        var valuations = await service.GetForPositionsAsync(positions);

        valuations[positions[0].Id].IndicativePositionValue.Should().Be(20_000m);
        valuations[positions[0].Id].SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        valuations[positions[1].Id].IndicativePositionValue.Should().Be(10_000m);
        valuations[positions[1].Id].SettlementAsset.Should().Be(SettlementAsset.From("USDC"));
        store.VerifyAll();
    }

    [Fact]
    public async Task Does_not_read_market_prices_for_closed_or_unsupported_positions()
    {
        var positions = new[]
        {
            CreatePosition("USDT", size: 1m) with { TrackingState = PositionTrackingState.Closed },
            CreatePosition("BTC", size: 1m),
        };
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        var service = new MarketMarkPriceService(store.Object, TimeProvider.System);

        var valuations = await service.GetForPositionsAsync(positions);

        valuations.Values.Should().OnlyContain(
            valuation => valuation.FreshnessStatus == MarketValuationFreshnessStatus.NotApplicable);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Portfolio_freshness_reads_shared_instrument_once_for_all_settlement_assets()
    {
        var observedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var positions = new[]
        {
            CreateValuationInput("BTCUSDT", "USDT", 0.5m),
            CreateValuationInput("BTCUSDT", "USDC", 0.25m),
        };
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.Is<IReadOnlyCollection<InstrumentId>>(symbols =>
                    symbols.Count == 1 && symbols.Contains(InstrumentId.From("BTCUSDT"))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreatePrice(observedAt, 40_000m)]);
        var service = new MarketMarkPriceService(
            store.Object,
            new MutableTimeProvider(observedAt.AddSeconds(1)));

        var freshness = await service.GetPortfolioFreshnessAsync(positions, positionsFullyReconciled: true);

        freshness.Status.Should().Be(MarketValuationFreshnessStatus.Fresh);
        freshness.CalculatedAt.Should().Be(observedAt.AddSeconds(1));
        store.VerifyAll();
    }

    [Fact]
    public async Task Portfolio_freshness_is_partial_when_position_composition_is_incomplete()
    {
        var observedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var position = CreateValuationInput("BTCUSDT", "USDT", 0.5m);
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.IsAny<IReadOnlyCollection<InstrumentId>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreatePrice(observedAt, 40_000m)]);
        var service = new MarketMarkPriceService(
            store.Object,
            new MutableTimeProvider(observedAt.AddSeconds(1)));

        var freshness = await service.GetPortfolioFreshnessAsync([position], positionsFullyReconciled: false);

        freshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
        store.VerifyAll();
    }

    [Fact]
    public async Task Portfolio_freshness_is_not_applicable_only_for_a_complete_empty_scope()
    {
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        var service = new MarketMarkPriceService(store.Object, TimeProvider.System);

        var complete = await service.GetPortfolioFreshnessAsync([], positionsFullyReconciled: true);
        var incomplete = await service.GetPortfolioFreshnessAsync([], positionsFullyReconciled: false);

        complete.Status.Should().Be(MarketValuationFreshnessStatus.NotApplicable);
        incomplete.Status.Should().Be(MarketValuationFreshnessStatus.Unknown);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Portfolio_freshness_is_partial_for_mixed_fresh_and_unknown_positions()
    {
        var observedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var positions = new[]
        {
            CreateValuationInput("BTCUSDT", "USDT", 0.5m),
            CreateValuationInput("ETHUSDT", "USDC", 1m),
        };
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.Is<IReadOnlyCollection<InstrumentId>>(symbols =>
                    symbols.Count == 2
                    && symbols.Contains(InstrumentId.From("BTCUSDT"))
                    && symbols.Contains(InstrumentId.From("ETHUSDT"))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreatePrice(observedAt, 40_000m)]);
        var service = new MarketMarkPriceService(
            store.Object,
            new MutableTimeProvider(observedAt.AddSeconds(1)));

        var freshness = await service.GetPortfolioFreshnessAsync(positions, positionsFullyReconciled: true);

        freshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
        store.VerifyAll();
    }

    [Fact]
    public async Task Portfolio_freshness_is_unknown_when_no_supported_position_has_a_price()
    {
        var position = CreateValuationInput("BTCUSDT", "USDT", 0.5m);
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.IsAny<IReadOnlyCollection<InstrumentId>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new MarketMarkPriceService(store.Object, TimeProvider.System);

        var freshness = await service.GetPortfolioFreshnessAsync([position], positionsFullyReconciled: true);

        freshness.Status.Should().Be(MarketValuationFreshnessStatus.Unknown);
        store.VerifyAll();
    }

    [Fact]
    public async Task Portfolio_freshness_is_stale_when_every_supported_price_is_stale()
    {
        var observedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var position = CreateValuationInput("BTCUSDT", "USDT", 0.5m);
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.IsAny<IReadOnlyCollection<InstrumentId>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreatePrice(observedAt, 40_000m)]);
        var service = new MarketMarkPriceService(
            store.Object,
            new MutableTimeProvider(observedAt.AddSeconds(6)));

        var freshness = await service.GetPortfolioFreshnessAsync([position], positionsFullyReconciled: true);

        freshness.Status.Should().Be(MarketValuationFreshnessStatus.Stale);
        store.VerifyAll();
    }

    [Fact]
    public async Task Read_freshness_transitions_from_fresh_to_stale_to_fresh_without_mutating_the_position()
    {
        var observedAt = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var position = CreatePosition("USDT", size: 0.5m);
        IReadOnlyCollection<MarketMarkPrice> prices =
        [
            CreatePrice(observedAt, 40_000m),
        ];
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.IsAny<IReadOnlyCollection<InstrumentId>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => prices);
        var clock = new MutableTimeProvider(observedAt.AddSeconds(1));
        var service = new MarketMarkPriceService(store.Object, clock);
        var originalPosition = position;

        var fresh = await service.GetForPositionsAsync([position]);
        clock.SetUtcNow(observedAt.AddSeconds(6));
        var stale = await service.GetForPositionsAsync([position]);
        prices = [CreatePrice(observedAt.AddSeconds(6), 41_000m)];
        var freshAgain = await service.GetForPositionsAsync([position]);

        fresh[position.Id].FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Fresh);
        stale[position.Id].FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Stale);
        stale[position.Id].MarkPrice.Should().Be(40_000m);
        freshAgain[position.Id].FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Fresh);
        freshAgain[position.Id].MarkPrice.Should().Be(41_000m);
        position.Should().BeSameAs(originalPosition);
        position.MarkPrice.Should().Be(30_000m);
        store.Verify(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.IsAny<IReadOnlyCollection<InstrumentId>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    private static PositionReadListItem CreatePosition(string settlementAsset, decimal size) =>
        new(
            PositionId.New(),
            ExchangeAccountId.New(),
            "BTCUSDT",
            SettlementAsset.From(settlementAsset),
            PositionSide.Long,
            PositionTrackingState.Active,
            size,
            AverageEntryPrice: 30_000m,
            MarkPrice: 30_000m,
            PositionValue: 30_000m * size,
            UnrealizedPnl: 0m,
            Leverage: 1m,
            LiquidationPrice: null,
            FirstDetectedAt: DateTimeOffset.UtcNow,
            LastObservedAt: DateTimeOffset.UtcNow,
            ClosedAt: null)
        {
            ExchangeId = ExchangeId.Bybit,
            MarketCategory = MarketCategory.Linear,
        };

    private static PositionMarketValuationInput CreateValuationInput(
        string symbol,
        string settlementAsset,
        decimal size) =>
        new(
            PositionId.New(),
            ExchangeId.Bybit,
            MarketCategory.Linear,
            symbol,
            SettlementAsset.From(settlementAsset),
            PositionTrackingState.Active,
            size);

    private static MarketMarkPrice CreatePrice(DateTimeOffset observedAt, decimal price) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = InstrumentId.From("BTCUSDT"),
            MarkPrice = price,
            ObservedAt = observedAt,
            ReceivedAt = observedAt,
            Source = MarketMarkPriceSource.PublicTicker,
        };

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
