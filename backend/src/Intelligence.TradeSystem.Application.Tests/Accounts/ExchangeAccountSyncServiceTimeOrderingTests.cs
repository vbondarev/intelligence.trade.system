using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Tests.Realtime;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Portfolio;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

/// <summary>
/// Порядок времени полной REST-синхронизации относительно фактов потока и сводок, записанных
/// экземпляром с опережающими часами.
/// </summary>
public sealed class ExchangeAccountSyncServiceTimeOrderingTests
{
    [Fact]
    public async Task Full_Rest_After_A_Newer_Stream_Snapshot_Becomes_The_Latest_Portfolio()
    {
        var world = new RealtimeTestWorld();
        var previous = await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(10), clockAt: T0.AddSeconds(100));
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        result.FullReconciliationCommitted.Should().BeTrue();
        var latest = world.LatestPortfolio!;
        latest.Should().BeSameAs(world.Portfolios[^1]);
        latest.Should().BeEquivalentTo(result.PortfolioState);
        latest.CalculatedAt.Should().Be(previous.CalculatedAt.AddTicks(TimeSpan.TicksPerMicrosecond));
        latest.Capital.ObservedAt.Should().Be(T0.AddSeconds(20));
        latest.Positions.Should().ContainSingle().Which.Size.Should().Be(2m);
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.LastObservedAt.Should().Be(T0.AddSeconds(20));
        world.Account.LastAppliedPositionsObservationAt.Should().Be(T0.AddSeconds(20));
        world.Account.LastAppliedBalanceObservationAt.Should().Be(T0.AddSeconds(20));
    }

    [Theory]
    [InlineData(OpenPositionsObservationStatus.Partial)]
    [InlineData(OpenPositionsObservationStatus.Failed)]
    public async Task Degraded_Rest_After_A_Newer_Stream_Snapshot_Becomes_The_Latest_Portfolio(
        OpenPositionsObservationStatus status)
    {
        var world = new RealtimeTestWorld();
        var previous = await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(10), clockAt: T0.AddSeconds(100));
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.Status = status;
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        world.Portfolios.Should().HaveCount(2);
        var latest = world.LatestPortfolio!;
        latest.Should().BeSameAs(world.Portfolios[^1]);
        latest.CalculatedAt.Should().BeAfter(previous.CalculatedAt);
        latest.Capital.ObservedAt.Should().Be(T0.AddSeconds(20));
        latest.PositionsFullyReconciled.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 5)]
    [InlineData(5, 3)]
    public async Task Equal_And_Sub_Microsecond_Rest_Timestamps_Keep_Snapshots_Strictly_Ordered(
        long streamClockTicks,
        long restClockTicks)
    {
        var world = new RealtimeTestWorld();
        var previous = await ApplyStreamOpenAsync(
            world,
            observedAt: T0.AddSeconds(10),
            clockAt: T0.AddSeconds(100).AddTicks(streamClockTicks));
        world.Clock.UtcNow = T0.AddSeconds(100).AddTicks(restClockTicks);
        world.Provider.SetExchangePosition(OpenPosition());

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        var latest = world.LatestPortfolio!;
        latest.Should().BeSameAs(world.Portfolios[^1]);
        latest.CalculatedAt.Should().BeAfter(previous.CalculatedAt);
        latest.CalculatedAt.Should().BeOnOrAfter(world.Clock.UtcNow);
        (latest.CalculatedAt.UtcTicks % TimeSpan.TicksPerMicrosecond).Should().Be(0);
    }

    [Fact]
    public async Task Repeated_Rest_Synchronizations_Keep_The_Newest_Snapshot_Latest()
    {
        var world = new RealtimeTestWorld();
        await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(10), clockAt: T0.AddSeconds(100));
        world.Provider.SetExchangePosition(OpenPosition());
        var service = world.CreateSyncService();
        world.Clock.UtcNow = T0.AddSeconds(20);

        var first = await service.SynchronizeAsync(world.UserId, world.AccountId);
        var repeated = await service.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = T0.AddSeconds(21);
        var next = await service.SynchronizeAsync(world.UserId, world.AccountId);

        first.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        repeated.Outcome.Should().Be(ExchangeAccountSyncOutcome.AlreadyApplied);
        next.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        world.Portfolios.Should().HaveCount(3);
        world.Portfolios.Select(portfolio => portfolio.CalculatedAt).Should().BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
        world.LatestPortfolio.Should().BeSameAs(world.Portfolios[^1]);
        world.LatestPortfolio!.Should().BeEquivalentTo(next.PortfolioState);
    }

    [Fact]
    public async Task Cas_Retry_Rereads_The_Previous_Snapshot_Written_Between_Attempts()
    {
        var world = new RealtimeTestWorld();
        await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(10), clockAt: T0.AddSeconds(100));
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.AccountSaveConflictsToInject = 1;
        var attempts = 0;
        world.BeforeTransactionExecuteAsync = async cancellationToken =>
        {
            if (++attempts != 2)
            {
                return;
            }

            world.BeforeTransactionExecuteAsync = null;
            var wallet = await world.CreateWalletService()
                .ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(150)), cancellationToken);
            wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        };

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        attempts.Should().Be(2);
        var walletSnapshot = world.Portfolios[^2];
        walletSnapshot.Capital.ObservedAt.Should().Be(T0.AddSeconds(150));
        var latest = world.LatestPortfolio!;
        latest.Should().BeSameAs(world.Portfolios[^1]);
        latest.CalculatedAt.Should().Be(walletSnapshot.CalculatedAt.AddTicks(TimeSpan.TicksPerMicrosecond));
        latest.Capital.Should().BeEquivalentTo(walletSnapshot.Capital);
        latest.Positions.Should().ContainSingle().Which.Size.Should().Be(2m);
    }

    [Fact]
    public async Task Full_Rest_Older_Than_A_Stream_Fact_After_A_Clock_Rollback_Keeps_The_Fact()
    {
        var world = new RealtimeTestWorld();
        var previous = await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(100), clockAt: T0.AddSeconds(101));
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        result.Account!.LastError.Should().Be("positions_ambiguous");
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.Size.Should().Be(1m);
        position.LastObservedAt.Should().Be(T0.AddSeconds(100));
        position.Changes.Should().ContainSingle();
        var latest = world.LatestPortfolio!;
        latest.Should().BeSameAs(world.Portfolios[^1]);
        latest.CalculatedAt.Should().BeAfter(previous.CalculatedAt);
        latest.Capital.ObservedAt.Should().Be(T0.AddSeconds(20));
        latest.Positions.Should().ContainSingle().Which.Size.Should().Be(1m);
        world.Account.LastAppliedPositionsObservationAt.Should().Be(T0.AddSeconds(20));
    }

    /// <summary>
    /// R-208-01: Failed positions + Applied watermark не выставляет AcceptedPositionsObservation.
    /// </summary>
    [Fact]
    public async Task Failed_Positions_With_Applied_Watermark_Does_Not_Accept_Positions_Observation()
    {
        var world = new RealtimeTestWorld();
        await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(10), clockAt: T0.AddSeconds(10));
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        result.AcceptedPositionsObservation.Should().BeFalse();
        result.FullReconciliationCommitted.Should().BeFalse();
        world.Account.LastAppliedPositionsObservationAt.Should().Be(T0.AddSeconds(20));
        world.ActivePositions.Single().TrackingState.Should().Be(PositionTrackingState.Unknown);
    }

    /// <summary>
    /// R-208-01: Complete/Partial при Applied сохраняют свидетельство принятого наблюдения позиций.
    /// </summary>
    [Theory]
    [InlineData(OpenPositionsObservationStatus.Complete)]
    [InlineData(OpenPositionsObservationStatus.Partial)]
    public async Task Accepted_Positions_Statuses_With_Applied_Watermark_Keep_Observation_Evidence(
        OpenPositionsObservationStatus status)
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.Status = status;
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.AcceptedPositionsObservation.Should().BeTrue();
        if (status == OpenPositionsObservationStatus.Complete)
        {
            result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
            result.FullReconciliationCommitted.Should().BeTrue();
        }
        else
        {
            result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
            result.FullReconciliationCommitted.Should().BeFalse();
        }
    }

    /// <summary>
    /// R-208-01: Stale Complete не создаёт свидетельство принятия наблюдения позиций.
    /// </summary>
    [Fact]
    public async Task Stale_Complete_Positions_Do_Not_Accept_Positions_Observation()
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.SetExchangePosition(OpenPosition());
        var first = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        first.AcceptedPositionsObservation.Should().BeTrue();

        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        var stale = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        stale.Outcome.Should().Be(ExchangeAccountSyncOutcome.Superseded);
        stale.AcceptedPositionsObservation.Should().BeFalse();
        world.ActivePositions.Single().Size.Should().Be(1m);
    }

    /// <summary>
    /// R-208-02: Complete с равным временем и противоречащим размером не подтверждает полноту.
    /// </summary>
    [Fact]
    public async Task Equal_Timestamp_Conflicting_Complete_Does_Not_Commit_Full_Reconciliation()
    {
        var world = new RealtimeTestWorld();
        await ApplyStreamOpenAsync(world, observedAt: T0.AddSeconds(10), clockAt: T0.AddSeconds(10));
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(20);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        result.FullReconciliationCommitted.Should().BeFalse();
        result.AcceptedPositionsObservation.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.LatestPortfolio!.PositionsFullyReconciled.Should().BeFalse();
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Full_Rest_Older_Than_The_Latest_History_Change_Does_Not_Rewrite_History()
    {
        var world = new RealtimeTestWorld();
        var staleAt = T0.AddMinutes(10);
        world.SeedPosition("BTCUSDT", T0, T0, seeded => seeded.RefreshFreshness(staleAt, TimeSpan.FromMinutes(5)));
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Clock.UtcNow = T0.AddMinutes(6);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        var position = world.Positions.Should().ContainSingle().Subject;
        position.TrackingState.Should().Be(PositionTrackingState.Stale);
        position.Size.Should().Be(1m);
        position.Changes.Select(change => (change.Kind, change.OccurredAt)).Should().Equal(
            (PositionChangeKind.New, T0),
            (PositionChangeKind.MarkedStale, staleAt));
        world.Events.Should().NotContain(applicationEvent =>
            applicationEvent is PositionChangedEventV1 || applicationEvent is PositionClosedEventV1);
    }

    private static async Task<PortfolioState> ApplyStreamOpenAsync(
        RealtimeTestWorld world,
        DateTimeOffset observedAt,
        DateTimeOffset clockAt)
    {
        world.Clock.UtcNow = clockAt;
        var result = await world.CreatePositionService()
            .ApplyAsync(world.UserId, 1, world.OpenObservation(observedAt, 1));
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        return world.LatestPortfolio!;
    }
}
