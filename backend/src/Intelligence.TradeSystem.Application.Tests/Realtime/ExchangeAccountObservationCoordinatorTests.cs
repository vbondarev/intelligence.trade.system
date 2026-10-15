using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.DependencyInjection;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class ExchangeAccountObservationCoordinatorTests
{
    [Fact]
    public void AddApplication_Routes_All_Rest_Synchronizations_Through_One_Coordinator()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        using var scope = provider.CreateScope();

        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();

        scope.ServiceProvider.GetRequiredService<IExchangeAccountSyncService>().Should().BeSameAs(coordinator);
        scope.ServiceProvider.GetRequiredService<RealtimePositionReconciliationService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<RealtimeWalletObservationService>().Should().NotBeNull();
    }

    [Fact]
    public async Task Successful_Full_Sync_Clears_Recovery_Flag_And_Older_Buffered_Open_Is_Stale()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(world.Clock.UtcNow);
        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanFullSync);
        world.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task Buffer_Overflow_Marks_Recovery_And_Runs_Full_Sync_Instead_Of_Applying_Buffer()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 2);

        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddMinutes(2), 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddMinutes(3), 2)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddMinutes(4), 3)).Should().BeFalse();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var overflow = results.Should().ContainSingle().Subject;
        overflow.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        overflow.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.Provider.PositionRequests.Should().Equal([null]);
        world.Positions.Should().BeEmpty();
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Observation_Received_During_Full_Sync_Is_Buffered_And_Confirmed_With_Rest()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var enqueued = false;
        world.Provider.OnPositionsRequest = () =>
        {
            if (!enqueued)
            {
                enqueued = coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(world.Clock.UtcNow, 1, OpenPosition(size: 2m)));
            }
        };

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        enqueued.Should().BeTrue();
        var result = results.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.Provider.PositionRequests.Should().Equal(null, "BTCUSDT");
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(1m);
    }

    [Fact]
    public async Task Ambiguous_Observation_Triggers_Recovery_Sync()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        coordinator.TryEnqueue(
                world.UserId,
                1,
                world.OpenObservation(T0, 1) with { Kind = PrivatePositionObservationKind.Ambiguous })
            .Should()
            .BeTrue();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservation);
        world.Provider.PositionRequests.Should().Equal([null]);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Newer_Generation_Discards_Buffered_Observations_Of_Previous_Generation()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();

        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        world.AdvanceGeneration();
        coordinator.TryEnqueue(world.UserId, 2, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(2), 3)).Should().BeFalse();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
    }

    [Fact]
    public async Task Wallet_Newer_Than_Rest_Balance_Received_During_Full_Sync_Is_Applied()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restAt = world.Clock.UtcNow;
        var walletAt = restAt.AddSeconds(2);
        EnqueueDuringRestRequest(
            world,
            coordinator,
            world.WalletObservation(walletAt, totalEquity: 1_100m),
            completedAt: restAt.AddSeconds(4));

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var portfoliosAfterSync = world.Portfolios.Count;
        var eventsAfterSync = world.Events.Count;
        world.Clock.UtcNow = restAt.AddSeconds(5);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Portfolios.Should().HaveCount(portfoliosAfterSync + 1);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_100m);
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(walletAt);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(walletAt);
        world.Events.Skip(eventsAfterSync).Should().ContainSingle().Which.Should().BeOfType<PortfolioUpdatedEventV1>();
    }

    [Fact]
    public async Task Wallet_Older_Than_Rest_Balance_Does_Not_Change_Capital_Or_Outbox()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restAt = world.Clock.UtcNow;
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(restAt.AddSeconds(-1), totalEquity: 1_100m))
            .Should()
            .BeTrue();

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var portfoliosAfterSync = world.Portfolios.Count;
        var eventsAfterSync = world.Events.Count;
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var result = results.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanBalance);
        world.Portfolios.Should().HaveCount(portfoliosAfterSync);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(2_000m);
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(restAt);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(restAt);
        world.Events.Should().HaveCount(eventsAfterSync);
    }

    [Fact]
    public async Task Wallet_Is_Not_Lost_When_Rest_Balance_Fails()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var failedRestAt = T0.AddMinutes(2);
        world.Clock.UtcNow = failedRestAt;
        var walletAt = failedRestAt.AddSeconds(-1);
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(walletAt, totalEquity: 1_100m))
            .Should()
            .BeTrue();
        world.Provider.BalanceFails = true;

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_100m);
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(walletAt);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(failedRestAt);
    }

    [Fact]
    public async Task Unavailable_Rest_Does_Not_Turn_Wallet_Observations_Stale()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var failedRestAt = T0.AddMinutes(2);
        world.Clock.UtcNow = failedRestAt;
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(failedRestAt.AddSeconds(-1), totalEquity: 1_100m))
            .Should()
            .BeTrue();
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.Provider.BalanceFails = true;
        var duringRestAt = failedRestAt.AddSeconds(2);
        EnqueueDuringRestRequest(
            world,
            coordinator,
            world.WalletObservation(duringRestAt, totalEquity: 1_200m),
            completedAt: failedRestAt.AddSeconds(4));

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = failedRestAt.AddSeconds(5);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        results.Select(result => result.Outcome)
            .Should()
            .Equal(RealtimeObservationApplyOutcome.Applied, RealtimeObservationApplyOutcome.Applied);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_200m);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(duringRestAt);
    }

    [Fact]
    public async Task Wallet_With_The_Same_Watermark_Is_Duplicate_Without_Writes()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var restAt = world.Clock.UtcNow;
        var portfoliosAfterSync = world.Portfolios.Count;
        var eventsAfterSync = world.Events.Count;

        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(restAt)).Should().BeTrue();
        var atRestBalance = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = restAt.AddSeconds(30);
        var wallet = world.WalletObservation(world.Clock.UtcNow.AddSeconds(-1));
        coordinator.TryEnqueue(world.UserId, 1, wallet).Should().BeTrue();
        var newer = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        coordinator.TryEnqueue(world.UserId, 1, wallet).Should().BeTrue();
        var repeated = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        atRestBalance.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        newer.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        repeated.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.Portfolios.Should().HaveCount(portfoliosAfterSync + 1);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_000m);
        world.Events.Should().HaveCount(eventsAfterSync + 1);
    }

    [Fact]
    public async Task Wallet_Observations_Arriving_In_Reverse_Order_Do_Not_Regress_Capital()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var at = world.Clock.UtcNow;
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(at.AddSeconds(3), totalEquity: 1_300m)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(at.AddSeconds(1), totalEquity: 1_100m)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(at.AddSeconds(2), totalEquity: 1_200m)).Should().BeTrue();
        world.Clock.UtcNow = at.AddSeconds(4);

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Select(result => result.Outcome)
            .Should()
            .Equal(
                RealtimeObservationApplyOutcome.Applied,
                RealtimeObservationApplyOutcome.Stale,
                RealtimeObservationApplyOutcome.Stale);
        world.Portfolios.Should().ContainSingle().Which.Capital.TotalEquity.Should().Be(1_300m);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(at.AddSeconds(3));
        world.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Wallet_Observation_Does_Not_Change_Positions_Watermark_Or_Positions()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restAt = world.Clock.UtcNow;
        EnqueueDuringRestRequest(
            world,
            coordinator,
            world.WalletObservation(restAt.AddSeconds(2), totalEquity: 1_100m),
            completedAt: restAt.AddSeconds(4));
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var position = world.ActivePositions.Single();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Account.LastAppliedPositionsObservationAt.Should().Be(restAt);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(restAt.AddSeconds(2));
        var unchanged = world.ActivePositions.Should().ContainSingle().Subject;
        unchanged.Id.Should().Be(position.Id);
        unchanged.LastObservedAt.Should().Be(position.LastObservedAt);
        unchanged.Changes.Should().HaveCount(position.Changes.Count);
        world.Portfolios[^1].Positions.Should().ContainSingle();
        world.Cursor("BTCUSDT").Should().BeNull();
    }

    [Fact]
    public async Task Wallet_Observation_Of_Superseded_Generation_Is_Rejected_Without_Writes()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(T0)).Should().BeTrue();
        world.AdvanceGeneration();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.GenerationSuperseded);
        world.Portfolios.Should().BeEmpty();
        world.Events.Should().BeEmpty();
        world.Account.LastAppliedBalanceObservationAt.Should().BeNull();
        coordinator.TryEnqueue(world.UserId, 2, world.WalletObservation(T0.AddSeconds(1))).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(T0.AddSeconds(2))).Should().BeFalse();
    }

    [Fact]
    public async Task Wallet_Applied_After_Failed_Rest_Does_Not_Mark_The_Account_As_Reconciled()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(world.Clock.UtcNow.AddSeconds(-1)))
            .Should()
            .BeTrue();
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.Provider.BalanceFails = true;

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
        world.Account.LastSyncedAt.Should().BeNull();
        world.Account.LastError.Should().Be("balance_failed");
        world.Portfolios[^1].PositionsFullyReconciled.Should().BeFalse();
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_000m);
    }

    /// <summary>
    /// Помещает наблюдение кошелька в буфер во время REST-запроса позиций полной синхронизации:
    /// после REST-снимка баланса, но до завершения синхронизации в <paramref name="completedAt"/>.
    /// </summary>
    private static void EnqueueDuringRestRequest(
        RealtimeTestWorld world,
        ExchangeAccountObservationCoordinator coordinator,
        PrivateWalletObservation wallet,
        DateTimeOffset completedAt) =>
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = wallet.ReceivedAt;
            coordinator.TryEnqueue(world.UserId, 1, wallet).Should().BeTrue();
            world.Clock.UtcNow = completedAt;
        };

    [Fact]
    public void Non_Positive_Generation_Is_Rejected()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var act = () => coordinator.TryEnqueue(world.UserId, 0, world.OpenObservation(T0, 1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
