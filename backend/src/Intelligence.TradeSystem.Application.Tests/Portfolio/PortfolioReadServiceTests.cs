using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
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
        var positionId = PositionId.New();
        var summary = CreateSummary() with { SnapshotPositionIds = [positionId] };
        var projected = await GetLatestSummaryAsync(
            summary,
            [CreatePosition(positionId)]);

        projected.IsFresh.Should().Be(summary.IsFresh);
        projected.PositionsFullyReconciled.Should().Be(summary.PositionsFullyReconciled);
        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Fresh);
        projected.FactualFreshness.LastFullyReconciledAt.Should().Be(Now.AddMinutes(-1));
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Fresh);
        projected.ValuationFreshness.CalculatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task GetLatest_marks_fresh_prices_partial_when_current_position_set_has_additional_ids()
    {
        var snapshotPositionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [snapshotPositionId] },
            [
                CreatePosition(snapshotPositionId),
                CreatePosition(PositionId.New()),
            ]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_marks_fresh_prices_partial_when_position_id_is_replaced()
    {
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [PositionId.New()] },
            [CreatePosition(PositionId.New())]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_includes_unsupported_positions_in_the_composition_check()
    {
        var supportedPositionId = PositionId.New();
        var snapshotUnsupportedPositionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary(currentPositionCount: 2) with
            {
                SnapshotPositionIds = [supportedPositionId, snapshotUnsupportedPositionId],
            },
            [
                CreatePosition(supportedPositionId),
                CreatePosition(PositionId.New()) with { SettlementAsset = SettlementAsset.From("BTC") },
            ]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_does_not_confirm_matching_duplicate_position_ids()
    {
        var positionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary(currentPositionCount: 2) with { SnapshotPositionIds = [positionId, positionId] },
            [
                CreatePosition(positionId),
                CreatePosition(positionId),
            ]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_returns_unknown_when_snapshot_has_positions_but_current_scope_is_empty()
    {
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [PositionId.New()] },
            []);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Unknown);
    }

    [Fact]
    public async Task GetLatest_returns_not_applicable_for_a_confirmed_empty_position_set()
    {
        var projected = await GetLatestSummaryAsync(
            CreateSummary(currentPositionCount: 0) with { SnapshotPositionIds = [] },
            []);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Fresh);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.NotApplicable);
    }

    [Fact]
    public async Task GetLatest_marks_fresh_prices_partial_when_snapshot_ids_are_unavailable()
    {
        var projected = await GetLatestSummaryAsync(
            CreateSummary(),
            [CreatePosition(PositionId.New())]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Fresh);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_marks_factual_freshness_degraded_when_current_scope_loses_one_id()
    {
        var retainedId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary(currentPositionCount: 2) with { SnapshotPositionIds = [retainedId, PositionId.New()] },
            [CreatePosition(retainedId)]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetLatest_keeps_unknown_when_reconciliation_history_is_unavailable(bool hasState)
    {
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [PositionId.New()] },
            [CreatePosition(PositionId.New())],
            state => hasState ? state with { LastFullyReconciledAt = null } : null);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Unknown);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Theory]
    [InlineData(RealtimeObservationTransportStatus.Connected, true)]
    [InlineData(RealtimeObservationTransportStatus.Reconnecting, false)]
    public async Task GetLatest_keeps_degraded_priority_during_recovery_or_transport_failure(
        RealtimeObservationTransportStatus status,
        bool recoveryRequired)
    {
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [PositionId.New()], IsFresh = false },
            [CreatePosition(PositionId.New())],
            state => state with { TransportStatus = status, RecoveryRequired = recoveryRequired });

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Degraded);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_keeps_stale_factual_freshness_when_ids_match_and_snapshot_is_stale()
    {
        var positionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [positionId], IsFresh = false },
            [CreatePosition(positionId)]);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Stale);
        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Fresh);
    }

    [Theory]
    [InlineData(false, MarketValuationFreshnessStatus.Unknown)]
    [InlineData(true, MarketValuationFreshnessStatus.Stale)]
    public async Task GetLatest_keeps_factual_freshness_when_market_price_is_unavailable_or_stale(
        bool hasPrice,
        MarketValuationFreshnessStatus expectedValuation)
    {
        var positionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary() with { SnapshotPositionIds = [positionId] },
            [CreatePosition(positionId)],
            prices: symbols => hasPrice
                ? symbols.Select(symbol => CreateFreshPrice(symbol) with { ObservedAt = Now.AddHours(-1) }).ToArray()
                : []);

        projected.FactualFreshness.Status.Should().Be(FactualFreshnessStatus.Fresh);
        projected.ValuationFreshness.Status.Should().Be(expectedValuation);
    }

    [Fact]
    public async Task GetLatest_does_not_confirm_freshness_when_positions_are_not_fully_reconciled()
    {
        var positionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary(positionsFullyReconciled: false) with { SnapshotPositionIds = [positionId] },
            [CreatePosition(positionId)]);

        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    [Fact]
    public async Task GetLatest_checks_each_position_id_but_reads_a_shared_instrument_once()
    {
        var firstPositionId = PositionId.New();
        var secondPositionId = PositionId.New();
        var projected = await GetLatestSummaryAsync(
            CreateSummary(currentPositionCount: 2) with { SnapshotPositionIds = [firstPositionId, secondPositionId] },
            [
                CreatePosition(firstPositionId),
                CreatePosition(secondPositionId),
            ]);

        projected.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatus.Fresh);
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

    private static async Task<PortfolioReadSummary> GetLatestSummaryAsync(
        PortfolioReadSummary summary,
        IReadOnlyCollection<PositionMarketValuationInput> positions,
        Func<RealtimeObservationState, RealtimeObservationState?>? configureState = null,
        Func<InstrumentId[], MarketMarkPrice[]>? prices = null)
    {
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
        var observation = configureState is null ? state : configureState(state);
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        portfolioStore.Setup(x => x.GetLatestAsync(TestUserId, AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(true, summary));
        var observationStore = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        observationStore.Setup(x => x.GetAsync(TestUserId, AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(observation is null
                ? null
                : new Versioned<RealtimeObservationState>(observation, ConcurrencyVersion.Initial));
        var positionStore = new Mock<IPositionReadStore>(MockBehavior.Strict);
        positionStore.Setup(x => x.GetMarketValuationInputsByAccountAsync(
                TestUserId,
                AccountId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions.ToArray());
        var symbols = positions
            .Where(IsValuationApplicable)
            .Select(position => InstrumentId.From(position.Symbol))
            .Distinct()
            .ToArray();
        var markPriceStore = new Mock<IMarketMarkPriceStore>(MockBehavior.Strict);
        if (symbols.Length > 0)
        {
            markPriceStore.Setup(x => x.GetAsync(
                    ExchangeId.Bybit,
                    MarketCategory.Linear,
                    It.Is<IReadOnlyCollection<InstrumentId>>(actual =>
                        actual.Count == symbols.Length && actual.ToHashSet().SetEquals(symbols)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(prices?.Invoke(symbols) ?? symbols.Select(CreateFreshPrice).ToArray());
        }

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

        portfolioStore.VerifyAll();
        observationStore.VerifyAll();
        positionStore.VerifyAll();
        markPriceStore.VerifyAll();
        if (symbols.Length > 0)
        {
            markPriceStore.Verify(
                x => x.GetAsync(
                    ExchangeId.Bybit,
                    MarketCategory.Linear,
                    It.Is<IReadOnlyCollection<InstrumentId>>(actual =>
                        actual.Count == symbols.Length && actual.ToHashSet().SetEquals(symbols)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        var projected = Assert.IsType<PortfolioReadSummary>(result.Summary);
        projected.FactualFreshness.LastFullyReconciledAt.Should().Be(observation?.LastFullyReconciledAt);
        projected.Should().Be(summary with
        {
            FactualFreshness = projected.FactualFreshness,
            ValuationFreshness = projected.ValuationFreshness,
        });
        return projected;
    }

    private static bool IsValuationApplicable(PositionMarketValuationInput position) =>
        position.ExchangeId == ExchangeId.Bybit
        && position.MarketCategory == MarketCategory.Linear
        && position.TrackingState != PositionTrackingState.Closed
        && position.Size > 0m
        && (string.Equals(position.SettlementAsset.Value, "USDT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(position.SettlementAsset.Value, "USDC", StringComparison.OrdinalIgnoreCase));

    private static PositionMarketValuationInput CreatePosition(PositionId positionId) =>
        new(
            positionId,
            ExchangeId.Bybit,
            MarketCategory.Linear,
            "BTCUSDT",
            SettlementAsset.From("USDT"),
            PositionTrackingState.Active,
            0.5m);

    private static MarketMarkPrice CreateFreshPrice(InstrumentId symbol) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = symbol,
            MarkPrice = 40_000m,
            ObservedAt = Now.AddSeconds(-1),
            ReceivedAt = Now.AddSeconds(-1),
            Source = MarketMarkPriceSource.PublicTicker,
        };

    private static PortfolioReadSummary CreateSummary(
        bool positionsFullyReconciled = true,
        int currentPositionCount = 1) =>
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
            positionsFullyReconciled,
            IsComplete: true,
            IsFresh: true,
            CurrentPositionCount: currentPositionCount,
            Exposures: []);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
