using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PortfolioReadServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ExchangeAccountId AccountId = ExchangeAccountId.New();

    [Fact]
    public async Task GetLatest_projects_factual_and_account_wide_market_freshness()
    {
        var summary = CreateSummary();
        var state = new RealtimeObservationState
        {
            ExchangeAccountId = AccountId,
            Generation = 1,
            TransportStatus = RealtimeObservationTransportStatus.Connected,
            LastConnectedAt = Now.AddMinutes(-1),
            LastHeartbeatAt = Now.AddSeconds(-1),
            LastFullyReconciledAt = Now.AddMinutes(-1),
            RecoveryRequired = false,
        };
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        portfolioStore.Setup(x => x.GetLatestAsync(TestUserId, AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(true, summary));
        var observationStore = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        observationStore.Setup(x => x.GetAsync(TestUserId, AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<RealtimeObservationState>(state, ConcurrencyVersion.Initial));
        var positionStore = new Mock<IPositionReadStore>(MockBehavior.Strict);
        positionStore.Setup(x => x.GetMarketValuationInputsByAccountAsync(
                TestUserId,
                AccountId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PositionMarketValuationInput(
                    PositionId.New(),
                    ExchangeId.Bybit,
                    MarketCategory.Linear,
                    "BTCUSDT",
                    SettlementAsset.From("USDT"),
                    PositionTrackingState.Active,
                    0.5m),
            ]);
        var markPriceStore = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        markPriceStore.Setup(x => x.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                It.Is<IReadOnlyCollection<InstrumentId>>(symbols =>
                    symbols.Count == 1 && symbols.Contains(InstrumentId.From("BTCUSDT"))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MarketMarkPrice
                {
                    Exchange = ExchangeId.Bybit,
                    Category = MarketCategory.Linear,
                    Symbol = InstrumentId.From("BTCUSDT"),
                    MarkPrice = 40_000m,
                    ObservedAt = Now.AddSeconds(-1),
                    ReceivedAt = Now.AddSeconds(-1),
                    Source = MarketMarkPriceSource.PublicTicker,
                },
            ]);
        var service = new PortfolioReadService(
            portfolioStore.Object,
            new RealtimeObservationReadService(
                observationStore.Object,
                new RealtimeObservationReadOptions
                {
                    HeartbeatStaleAfter = TimeSpan.FromSeconds(60),
                },
                new FixedTimeProvider(Now)),
            positionStore.Object,
            new MarketMarkPriceService(markPriceStore.Object, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));

        var result = await service.GetLatestAsync(TestUserId, AccountId);

        var projected = Assert.IsType<PortfolioReadSummary>(result.Summary);
        projected.IsFresh.Should().Be(summary.IsFresh);
        projected.PositionsFullyReconciled.Should().Be(summary.PositionsFullyReconciled);
        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Fresh);
        projected.FactualFreshness.LastFullyReconciledAt.Should().Be(state.LastFullyReconciledAt);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Fresh);
        projected.ValuationFreshness.CalculatedAt.Should().Be(Now);
        portfolioStore.VerifyAll();
        observationStore.VerifyAll();
        positionStore.VerifyAll();
        markPriceStore.VerifyAll();
    }

    [Fact]
    public async Task GetLatest_preserves_no_snapshot_without_reading_realtime_or_positions()
    {
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        portfolioStore.Setup(x => x.GetLatestAsync(TestUserId, AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(true, null));
        var observationStore = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        var positionStore = new Mock<IPositionReadStore>(MockBehavior.Strict);
        var markPriceStore = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        var service = new PortfolioReadService(
            portfolioStore.Object,
            new RealtimeObservationReadService(
                observationStore.Object,
                new RealtimeObservationReadOptions
                {
                    HeartbeatStaleAfter = TimeSpan.FromSeconds(60),
                },
                new FixedTimeProvider(Now)),
            positionStore.Object,
            new MarketMarkPriceService(markPriceStore.Object, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));

        var result = await service.GetLatestAsync(TestUserId, AccountId);

        result.Should().Be(new PortfolioReadResult(true, null));
        portfolioStore.VerifyAll();
        observationStore.VerifyNoOtherCalls();
        positionStore.VerifyNoOtherCalls();
        markPriceStore.VerifyNoOtherCalls();
    }

    private static PortfolioReadSummary CreateSummary() =>
        new(
            AccountId,
            Now,
            1000m,
            800m,
            1000m,
            Now.AddMinutes(-1),
            200m,
            120m,
            80m,
            40m,
            10m,
            200m,
            800m,
            80m,
            20m,
            60m,
            null,
            PositionsFullyReconciled: true,
            IsComplete: true,
            IsFresh: true,
            CurrentPositionCount: 1,
            Exposures: []);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
