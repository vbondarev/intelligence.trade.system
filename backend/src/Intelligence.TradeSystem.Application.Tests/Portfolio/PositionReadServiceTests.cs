using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PositionReadServiceTests
{
    [Fact]
    public void Query_defaults_to_the_active_working_states_and_normalizes_symbol()
    {
        var query = PositionReadQuery.Create(
            ExchangeAccountId.New(),
            trackingState: null,
            symbol: "  btcusdt  ",
            side: PositionSide.Long,
            pageSize: 50,
            cursor: null);

        query.TrackingStates.Should().Equal(
            PositionTrackingState.Active,
            PositionTrackingState.Unknown,
            PositionTrackingState.Stale);
        query.Symbol.Should().Be("btcusdt");
        query.Side.Should().Be(PositionSide.Long);
    }

    [Fact]
    public async Task Service_forwards_only_typed_scope_and_query_to_the_store()
    {
        var userId = UserId.New();
        var query = PositionReadQuery.Create(
            null,
            PositionTrackingState.Closed,
            "BTCUSDT",
            PositionSide.Short,
            10,
            new PositionReadCursor(DateTimeOffset.UtcNow, PositionId.New()));
        var expected = new PositionReadPage([], null, false);
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        store.Setup(x => x.ListAsync(userId, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var service = new PositionReadService(
            store.Object,
            new MarketMarkPriceService(
                Mock.Of<IMarketMarkPriceStore>(),
                TimeProvider.System));

        var result = await service.ListAsync(userId, query);

        result.Should().BeSameAs(expected);
        store.VerifyAll();
    }

    [Fact]
    public async Task List_attaches_market_valuation_without_mutating_exchange_position_values()
    {
        var userId = UserId.New();
        var position = CreatePositionReadItem();
        var page = new PositionReadPage([position], null, false);
        var positionStore = new Mock<IPositionReadStore>(MockBehavior.Strict);
        positionStore.Setup(x => x.ListAsync(
                userId,
                It.IsAny<PositionReadQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        var markPriceStore = CreateMarkPriceStore(position.Symbol);
        var service = new PositionReadService(
            positionStore.Object,
            new MarketMarkPriceService(markPriceStore.Object, TimeProvider.System));

        var result = await service.ListAsync(
            userId,
            PositionReadQuery.Create(null, null, null, null, 10, null));

        var valuation = result.Items.Should().ContainSingle().Which.MarketValuation;
        valuation.Should().NotBeNull();
        valuation!.MarkPrice.Should().Be(40_000m);
        valuation.IndicativePositionValue.Should().Be(20_000m);
        valuation.SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        valuation.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Fresh);
        result.Items[0].LastObservedAt.Should().Be(position.LastObservedAt);
        result.Items[0].MarkPrice.Should().Be(position.MarkPrice);
        result.Items[0].PositionValue.Should().Be(position.PositionValue);
        position.MarketValuation.Should().BeNull();
        positionStore.VerifyAll();
        markPriceStore.VerifyAll();
    }

    [Fact]
    public async Task Detail_attaches_market_valuation_without_mutating_exchange_position_values()
    {
        var userId = UserId.New();
        var position = CreatePositionReadItem();
        var detail = new PositionReadDetail(
            position,
            MarketCategory.Linear,
            BreakEvenPrice: null,
            TakeProfit: null,
            StopLoss: null,
            TrailingStop: null);
        var positionStore = new Mock<IPositionReadStore>(MockBehavior.Strict);
        positionStore.Setup(x => x.GetByIdAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        var markPriceStore = CreateMarkPriceStore(position.Symbol);
        var service = new PositionReadService(
            positionStore.Object,
            new MarketMarkPriceService(markPriceStore.Object, TimeProvider.System));

        var result = await service.GetByIdAsync(userId, position.Id);

        result.Should().NotBeNull();
        result!.ListItem.MarketValuation.Should().NotBeNull();
        result.ListItem.MarketValuation!.IndicativePositionValue.Should().Be(20_000m);
        result.ListItem.LastObservedAt.Should().Be(position.LastObservedAt);
        result.ListItem.MarkPrice.Should().Be(position.MarkPrice);
        result.ListItem.PositionValue.Should().Be(position.PositionValue);
        position.MarketValuation.Should().BeNull();
        positionStore.VerifyAll();
        markPriceStore.VerifyAll();
    }

    private static PositionReadListItem CreatePositionReadItem() =>
        new(
            PositionId.New(),
            ExchangeAccountId.New(),
            "BTCUSDT",
            SettlementAsset.From("USDT"),
            PositionSide.Long,
            PositionTrackingState.Active,
            Size: 0.5m,
            AverageEntryPrice: 30_000m,
            MarkPrice: 30_100m,
            PositionValue: 15_050m,
            UnrealizedPnl: 50m,
            Leverage: 1m,
            LiquidationPrice: null,
            FirstDetectedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastObservedAt: DateTimeOffset.UtcNow,
            ClosedAt: null)
        {
            ExchangeId = ExchangeId.Bybit,
            MarketCategory = MarketCategory.Linear,
        };

    private static Mock<IMarketMarkPriceStore> CreateMarkPriceStore(string symbol)
    {
        var instrumentId = InstrumentId.From(symbol);
        var observedAt = DateTimeOffset.UtcNow;
        var store = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.Is<IReadOnlyCollection<InstrumentId>>(symbols =>
                    symbols.Count == 1 && symbols.Contains(instrumentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MarketMarkPrice
                {
                    Exchange = ExchangeId.Bybit,
                    Category = MarketCategory.Linear,
                    Symbol = instrumentId,
                    MarkPrice = 40_000m,
                    ObservedAt = observedAt,
                    ReceivedAt = observedAt,
                    Source = MarketMarkPriceSource.PublicTicker,
                },
            ]);
        return store;
    }
}
