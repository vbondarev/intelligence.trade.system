using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class RealtimeFreshnessProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(60);
    private static readonly ExchangeAccountId AccountId = ExchangeAccountId.New();

    [Fact]
    public void Missing_observation_state_is_unknown_even_when_the_snapshot_is_stale()
    {
        var result = ProjectFactualFreshness(
            CreateSummary(isFresh: false),
            state: null);

        result.Status.Should().Be(FactualFreshnessStatus.Unknown);
        result.LastFullyReconciledAt.Should().BeNull();
    }

    [Fact]
    public void Recovery_required_takes_priority_over_a_fresh_snapshot()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Connected) with
        {
            LastHeartbeatAt = Now,
            RecoveryRequired = true,
        };

        var result = ProjectFactualFreshness(CreateSummary(), state);

        result.Status.Should().Be(FactualFreshnessStatus.Degraded);
    }

    [Fact]
    public void Stale_connected_heartbeat_is_degraded()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Connected) with
        {
            LastHeartbeatAt = Now.AddSeconds(-61),
        };

        var result = ProjectFactualFreshness(CreateSummary(), state);

        result.Status.Should().Be(FactualFreshnessStatus.Degraded);
    }

    [Fact]
    public void Missing_full_reconciliation_timestamp_is_unknown()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Initializing) with
        {
            LastFullyReconciledAt = null,
        };

        var result = ProjectFactualFreshness(CreateSummary(), state);

        result.Status.Should().Be(FactualFreshnessStatus.Unknown);
    }

    [Fact]
    public void Future_full_reconciliation_timestamp_is_degraded()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Initializing) with
        {
            LastFullyReconciledAt = Now.AddSeconds(1),
        };

        var result = ProjectFactualFreshness(CreateSummary(), state);

        result.Status.Should().Be(FactualFreshnessStatus.Degraded);
    }

    [Fact]
    public void Incomplete_position_reconciliation_is_degraded()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Initializing);

        var result = ProjectFactualFreshness(
            CreateSummary(positionsFullyReconciled: false),
            state);

        result.Status.Should().Be(FactualFreshnessStatus.Degraded);
    }

    [Fact]
    public void Stale_snapshot_is_stale_with_healthy_transport()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Connected) with
        {
            LastHeartbeatAt = Now,
        };

        var result = ProjectFactualFreshness(CreateSummary(isFresh: false), state);

        result.Status.Should().Be(FactualFreshnessStatus.Stale);
    }

    [Fact]
    public void Stopped_observer_is_stale_after_a_successful_reconciliation()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Stopped);

        var result = ProjectFactualFreshness(CreateSummary(), state);

        result.Status.Should().Be(FactualFreshnessStatus.Stale);
    }

    [Fact]
    public void Initializing_observer_can_be_fresh_after_a_successful_full_reconciliation()
    {
        var state = CreateState(RealtimeObservationTransportStatus.Initializing);

        var result = ProjectFactualFreshness(CreateSummary(), state);

        result.Status.Should().Be(FactualFreshnessStatus.Fresh);
        result.LastFullyReconciledAt.Should().Be(state.LastFullyReconciledAt);
    }

    private static FactualFreshnessReadModel ProjectFactualFreshness(
        PortfolioReadSummary summary,
        RealtimeObservationState? state)
    {
        var observation = RealtimeFreshnessProjection.ProjectObservation(
            state,
            Now,
            HeartbeatStaleAfter);
        return RealtimeFreshnessProjection.ProjectFactualFreshness(summary, observation, Now);
    }

    private static PortfolioReadSummary CreateSummary(
        bool isFresh = true,
        bool positionsFullyReconciled = true) =>
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
            isFresh,
            CurrentPositionCount: 1,
            Exposures: []);

    private static RealtimeObservationState CreateState(RealtimeObservationTransportStatus status) =>
        new()
        {
            ExchangeAccountId = AccountId,
            Generation = 1,
            TransportStatus = status,
            LastConnectedAt = Now.AddMinutes(-1),
            LastHeartbeatAt = status == RealtimeObservationTransportStatus.Connected
                ? Now.AddSeconds(-1)
                : null,
            LastFullyReconciledAt = Now.AddMinutes(-2),
            RecoveryRequired = false,
        };
}
