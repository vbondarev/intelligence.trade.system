using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
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
    public async Task Successful_Full_Sync_Clears_Recovery_Flag_And_Older_Buffered_Open_Is_Confirmed_With_Rest()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        var fullSyncAt = world.Clock.UtcNow;

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = fullSyncAt.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(fullSyncAt);
        var result = results.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.Provider.PositionRequests.Should().Equal(null, "BTCUSDT");
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
    public async Task Wallet_With_The_Same_Watermark_And_Values_Is_Duplicate_Without_Writes()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var restAt = world.Clock.UtcNow;
        var portfoliosAfterSync = world.Portfolios.Count;
        var eventsAfterSync = world.Events.Count;

        coordinator.TryEnqueue(world.UserId, 1, RestBalanceWalletObservation(world, restAt)).Should().BeTrue();
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
    public async Task Wallet_With_The_Same_Watermark_But_Different_Values_Triggers_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(T0)).Should().BeTrue();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
        var restAt = world.Clock.UtcNow;
        var portfoliosAfterSync = world.Portfolios.Count;
        var eventsAfterSync = world.Events.Count;

        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(restAt)).Should().BeTrue();
        var recoveryFlags = new List<bool>();
        world.Provider.OnPositionsRequest = () => recoveryFlags.Add(world.State!.RecoveryRequired);
        world.Clock.UtcNow = restAt.AddSeconds(1);
        var conflicting = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var result = conflicting.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.BalanceConflict);
        recoveryFlags.Should().Equal(true);
        world.Provider.PositionRequests.Should().Equal([null, null]);
        world.Portfolios.Should().HaveCount(portfoliosAfterSync + 1, "only the recovery sync writes a snapshot");
        world.Events.Should().HaveCount(eventsAfterSync * 2, "only the recovery sync adds the events of a full sync");
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(2_000m);
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(restAt.AddSeconds(1));
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    private static PrivateWalletObservation RestBalanceWalletObservation(RealtimeTestWorld world, DateTimeOffset at) =>
        world.WalletObservation(
            at,
            world.Provider.Balance.TotalEquity,
            world.Provider.Balance.TotalAvailableBalance,
            world.Provider.Balance.TotalWalletBalance,
            world.Provider.Balance.TotalPerpUnrealizedPnl);

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

    [Fact]
    public async Task Failed_Recovery_Remains_Pending_And_Is_Retried_By_The_Next_Pass()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;

        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();

        var failed = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        failed.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.Provider.PositionRequests.Should().Equal([null]);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.Provider.PositionRequests.Should().Equal([null, null]);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Credentials_Unavailable_Does_Not_Acknowledge_Recovery()
    {
        var world = new RealtimeTestWorld { CredentialsUnavailable = true };
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.CredentialsUnavailable = false;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Rest_Exception_Does_Not_Acknowledge_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        world.Provider.PositionsException = new HttpRequestException("exchange unavailable");
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.PositionsException = null;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_Recovery_Flag_Persistence_Leaves_The_Local_Request_Pending()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        world.State!.RecoveryRequired.Should().BeFalse();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(2), 3)).Should().BeFalse();
        world.StateSaveFailuresToInject = 1;
        var requestsBefore = world.Provider.PositionRequests.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        world.Provider.PositionRequests.Should().HaveCount(requestsBefore, "REST cannot start before recovery is persisted");
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_During_Recovery_Leaves_The_Request_Pending()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Provider.OnPositionsRequestAsync = async cancellationToken =>
        {
            started.SetResult();
            await neverCompletes.Task.WaitAsync(cancellationToken);
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();
        using var cancellation = new CancellationTokenSource();
        var processing = coordinator.ProcessPendingAsync(world.UserId, world.AccountId, cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.OnPositionsRequestAsync = null;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_Requested_During_Sync_Is_Not_Acknowledged_By_The_Older_Sync()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(2), 3)).Should().BeTrue();
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(3), 4)).Should().BeFalse();
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.Provider.PositionRequests.Should().Equal([null, null]);
    }

    [Fact]
    public async Task Successful_Recovery_Of_An_Older_Generation_Does_Not_Clear_The_Newer_Generation()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.AdvanceGeneration();
            coordinator.TryEnqueue(world.UserId, 2, world.OpenObservation(T0.AddSeconds(2), 3)).Should().BeTrue();
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Unresolved_Recovery_Blocks_Buffered_Observations_Until_A_Successful_Retry()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeFalse();

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(2), 3)).Should().BeTrue();
        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.Positions.Should().BeEmpty();
        world.State!.RecoveryRequired.Should().BeFalse();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(3), 4)).Should().BeTrue();
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.Positions.Count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task A_Pass_Processes_At_Most_Buffer_Capacity_And_Leaves_The_Remainder_For_The_Next_Pass()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 2);
        var firstTransactionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstTransaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.BeforeTransactionExecuteAsync = async cancellationToken =>
        {
            world.BeforeTransactionExecuteAsync = null;
            firstTransactionStarted.SetResult();
            await releaseFirstTransaction.Task.WaitAsync(cancellationToken);
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        var firstPass = coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        await firstTransactionStarted.Task;
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(2), 3)).Should().BeTrue();
        releaseFirstTransaction.SetResult();

        var firstResults = await firstPass;
        var secondResults = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        firstResults.Should().HaveCount(2);
        secondResults.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(3);
    }

    [Fact]
    public async Task A_Bounded_Pass_Releases_The_Gate_For_A_Waiting_Manual_Synchronization()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = new ExchangeAccountObservationCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            world.Clock,
            bufferCapacity: 1);
        var transactionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.BeforeTransactionExecuteAsync = async cancellationToken =>
        {
            world.BeforeTransactionExecuteAsync = null;
            transactionStarted.SetResult();
            await releaseTransaction.Task.WaitAsync(cancellationToken);
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        var processing = coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        await transactionStarted.Task;
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 2)).Should().BeTrue();
        var manualSync = coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        releaseTransaction.SetResult();

        await Task.WhenAll(processing, manualSync);

        world.Provider.PositionRequests.Should().Contain(request => request == null);
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should()
            .ContainSingle()
            .Which
            .Outcome
            .Should()
            .Be(RealtimeObservationApplyOutcome.Stale);
    }

    [Theory]
    [InlineData(OpenPositionsObservationStatus.Failed)]
    [InlineData(OpenPositionsObservationStatus.Complete)]
    public async Task Recovery_Is_Not_Acknowledged_By_Fresh_Balance_With_Superseded_Positions(
        OpenPositionsObservationStatus positionsStatus)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var fullSyncAt = world.Clock.UtcNow;
        await EstablishReconciledGenerationAsync(world, coordinator);
        RequestRecoveryByOverflow(world, coordinator);
        world.Provider.Status = positionsStatus;
        world.Provider.SnapshotObservedAt = fullSyncAt.AddSeconds(-1);
        world.Clock.UtcNow = fullSyncAt.AddSeconds(1);

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        // Полнота позиций унаследована от прежней сводки, но этим запросом не подтверждена.
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(fullSyncAt.AddSeconds(1));
        world.Portfolios[^1].PositionsFullyReconciled.Should().BeTrue();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(fullSyncAt);

        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Provider.SnapshotObservedAt = null;
        world.Clock.UtcNow = fullSyncAt.AddSeconds(2);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(fullSyncAt.AddSeconds(2));
    }

    [Fact]
    public async Task Recovery_Is_Not_Acknowledged_By_Complete_Positions_Without_Full_Coverage()
    {
        var world = new RealtimeTestWorld();
        world.SeedPosition(Position.Create(
            ExchangePositionKey.Create(world.AccountId, InstrumentId.From("BTCUSD"), PositionSide.Long, 0),
            MarketCategory.Inverse,
            Usdt,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            unrealizedPnl: 0m));
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        RequestRecoveryByOverflow(world, coordinator);

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.FullReconciliationCommitted.Should().BeFalse();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
    }

    [Fact]
    public async Task Recovery_Is_Not_Acknowledged_When_The_Full_Sync_Transaction_Fails_To_Commit()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        RequestRecoveryByOverflow(world, coordinator);
        world.CommitFailuresToInject = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        world.Portfolios.Should().BeEmpty();
        world.Account.LastAppliedPositionsObservationAt.Should().BeNull();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Overflow_During_A_Regular_Sync_Persists_Recovery_Of_A_Reconciled_Account()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var firstSyncAt = world.Clock.UtcNow;
        await EstablishReconciledGenerationAsync(world, coordinator);
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(40), 40)).Should().BeFalse();
        };
        world.Clock.UtcNow = firstSyncAt.AddSeconds(1);

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        sync.FullReconciliationCommitted.Should().BeTrue();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(firstSyncAt);

        world.Clock.UtcNow = firstSyncAt.AddSeconds(2);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(firstSyncAt.AddSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overflow_During_The_Reconciled_Write_Persists_Recovery_Again(bool recoveryRequestedAtStart)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var firstSyncAt = world.Clock.UtcNow;
        await EstablishReconciledGenerationAsync(world, coordinator);
        if (recoveryRequestedAtStart)
        {
            RequestRecoveryByOverflow(world, coordinator);
        }

        world.BeforeStateSave = state =>
        {
            if (!state.RecoveryRequired)
            {
                world.BeforeStateSave = null;
                coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(40), 40)).Should().BeFalse();
            }
        };
        world.Clock.UtcNow = firstSyncAt.AddSeconds(1);

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        sync.FullReconciliationCommitted.Should().BeTrue();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(firstSyncAt.AddSeconds(1));
        var requestsBefore = world.Provider.PositionRequests.Count;

        world.Clock.UtcNow = firstSyncAt.AddSeconds(2);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null]);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Lost_Cas_On_The_Reconciled_Write_Does_Not_Acknowledge_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        RequestRecoveryByOverflow(world, coordinator);
        world.StateConflictsToInject = 3;

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.Provider.PositionRequests.Should().Equal([null, null]);
    }

    [Fact]
    public async Task Failed_Reconciled_Write_Does_Not_Acknowledge_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        RequestRecoveryByOverflow(world, coordinator);
        world.StateSaveFailuresToInject = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        world.Portfolios.Should().NotBeEmpty("the REST transaction committed before the state write failed");
        world.State!.RecoveryRequired.Should().BeTrue();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.Provider.PositionRequests.Should().Equal([null, null]);
    }

    [Fact]
    public async Task Cancellation_During_The_Reconciled_Write_Does_Not_Acknowledge_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        RequestRecoveryByOverflow(world, coordinator);
        using var cancellation = new CancellationTokenSource();
        world.BeforeStateSave = state =>
        {
            world.BeforeStateSave = null;
            cancellation.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId, cancellation.Token));

        world.State!.RecoveryRequired.Should().BeTrue();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_Recoveries_Are_Each_Persisted_And_Acknowledged()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var recoveryFlags = new List<bool>();
        world.Provider.OnPositionsRequest = () => recoveryFlags.Add(world.State!.RecoveryRequired);

        for (var round = 0; round < 3; round++)
        {
            RequestRecoveryByOverflow(world, coordinator);
            world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
            (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
                .Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
            world.State!.RecoveryRequired.Should().BeFalse();
        }

        recoveryFlags.Should().Equal(true, true, true);
        world.Provider.PositionRequests.Should().Equal([null, null, null]);
    }

    [Fact]
    public async Task Delayed_Open_After_Failed_Sync_Without_Rest_Confirmation_Is_Recovered_By_Full_Sync()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 4);
        var failedSyncAt = T0.AddSeconds(10);
        world.Clock.UtcNow = failedSyncAt;
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        (await coordinator.SynchronizeAsync(world.UserId, world.AccountId))
            .FullReconciliationCommitted.Should().BeFalse();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(9), 1)).Should().BeTrue();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = failedSyncAt.AddSeconds(1);

        var unavailable = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        unavailable.Should().ContainSingle().Which.ReasonCode
            .Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.Positions.Should().BeEmpty();
        world.Cursor("BTCUSDT").Should().BeNull();
        world.State!.RecoveryRequired.Should().BeTrue();

        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Clock.UtcNow = failedSyncAt.AddSeconds(2);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.ActivePositions.Should().ContainSingle();
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_And_Foreign_Accounts_Do_Not_Accumulate_Channels()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);

        for (var i = 0; i < 100; i++)
        {
            (await coordinator.SynchronizeAsync(world.UserId, ExchangeAccountId.New()))
                .Outcome.Should().Be(ExchangeAccountSyncOutcome.NotFound);
            (await coordinator.SynchronizeAsync(UserId.New(), world.AccountId))
                .Outcome.Should().Be(ExchangeAccountSyncOutcome.NotFound);
            (await coordinator.ProcessPendingAsync(world.UserId, ExchangeAccountId.New())).Should().BeEmpty();
        }

        coordinator.ChannelCount.Should().Be(0);
        world.Provider.PositionRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Synchronizations_Without_Realtime_Observation_Do_Not_Retain_The_Channel()
    {
        var world = new RealtimeTestWorld(observed: false);
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);

        for (var i = 0; i < 3; i++)
        {
            world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
            (await coordinator.SynchronizeAsync(world.UserId, world.AccountId))
                .Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
            coordinator.ChannelCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task Observed_Account_Keeps_Its_Channel_And_Rest_Windows()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var enqueuedDuringSync = false;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            enqueuedDuringSync = coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(world.Clock.UtcNow, 1));
        };

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        coordinator.ChannelCount.Should().Be(1);
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        enqueuedDuringSync.Should().BeTrue();
        results.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        coordinator.ChannelCount.Should().Be(1);
    }

    [Fact]
    public async Task Disabled_Account_Channel_Is_Released_After_Pending_Observations_Are_Drained()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 2);
        world.DisableAccount();

        (await coordinator.SynchronizeAsync(world.UserId, world.AccountId))
            .Outcome.Should().Be(ExchangeAccountSyncOutcome.AccountDisabled);
        coordinator.ChannelCount.Should().Be(0);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(T0)).Should().BeTrue();
        coordinator.ChannelCount.Should().Be(1, "pending observations must not be lost");

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.AccountUnavailable);
        coordinator.ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task Unhandled_Recovery_Of_An_Unavailable_Observed_Account_Is_Not_Dropped()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await EstablishReconciledGenerationAsync(world, coordinator);
        RequestRecoveryByOverflow(world, coordinator);
        world.DisableAccount();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.RecoveryRequired.Should().BeTrue();
        coordinator.ChannelCount.Should().Be(1);
    }

    [Fact]
    public async Task Waiting_Operation_Keeps_The_Channel_So_One_Account_Never_Has_Two_Gates()
    {
        var world = new RealtimeTestWorld(observed: false);
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var active = 0;
        var maxActive = 0;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Provider.OnPositionsRequestAsync = async cancellationToken =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, current);
            if (firstStarted.TrySetResult())
            {
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else
            {
                await Task.Yield();
            }

            Interlocked.Decrement(ref active);
        };

        var first = coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        await firstStarted.Task;
        using var cancellation = new CancellationTokenSource();
        var cancelled = coordinator.SynchronizeAsync(world.UserId, world.AccountId, cancellation.Token);
        var waiting = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => coordinator.SynchronizeAsync(world.UserId, world.AccountId)))
            .ToArray();
        coordinator.ChannelCount.Should().Be(1);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        coordinator.ChannelCount.Should().Be(1, "the running and waiting operations still hold the channel");
        releaseFirst.SetResult();

        await first;
        await Task.WhenAll(waiting);

        maxActive.Should().Be(1);
        world.Provider.PositionRequests.Should().HaveCount(21);
        coordinator.ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancelled_Operations_Release_Their_Channel()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.SynchronizeAsync(world.UserId, ExchangeAccountId.New(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ProcessPendingAsync(world.UserId, ExchangeAccountId.New(), cancellation.Token));

        coordinator.ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_Recreation_Of_Released_Channels_Leaks_Nothing()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1_000);
        var unknownAccounts = Enumerable.Range(0, 8).Select(_ => ExchangeAccountId.New()).ToArray();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            var accountId = unknownAccounts[i % unknownAccounts.Length];
            if (i % 2 == 0)
            {
                await coordinator.SynchronizeAsync(world.UserId, accountId);
            }
            else
            {
                coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(T0) with { ExchangeAccountId = accountId });
                await coordinator.ProcessPendingAsync(world.UserId, accountId);
            }
        })));

        coordinator.ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task Startup_Sync_Without_Stream_Observations_Reconciles_The_Persisted_Generation()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = world.Clock.UtcNow;

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        sync.FullReconciliationCommitted.Should().BeTrue();
        world.State!.Generation.Should().Be(1);
        world.State.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(syncAt);
        coordinator.ChannelCount.Should().Be(1, "the observed account keeps its REST windows");
    }

    [Fact]
    public async Task Generation_Advanced_During_Rest_Without_Stream_Observations_Is_Reconciled_By_The_Next_Sync()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.AdvanceGeneration();
        };

        var first = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        first.FullReconciliationCommitted.Should().BeTrue();
        world.State!.Generation.Should().Be(2);
        world.State.RecoveryRequired.Should().BeTrue("the older generation sync does not prove the newer one");
        world.State.LastFullyReconciledAt.Should().BeNull();

        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(world.Clock.UtcNow);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeFalse();
        coordinator.TryEnqueue(world.UserId, 2, world.OpenObservation(T0, 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Released_Channel_Learns_A_Generation_Started_After_Its_Release()
    {
        var world = new RealtimeTestWorld(observed: false);
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        coordinator.ChannelCount.Should().Be(0);

        world.StartObservation();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(world.Clock.UtcNow);
        coordinator.ChannelCount.Should().Be(1);
    }

    [Fact]
    public async Task Foreign_User_Synchronization_Neither_Learns_Nor_Reconciles_The_Owner_Generation()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);

        (await coordinator.SynchronizeAsync(UserId.New(), world.AccountId))
            .Outcome.Should().Be(ExchangeAccountSyncOutcome.NotFound);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
        coordinator.ChannelCount.Should().Be(0);
        world.Provider.PositionRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Rest_Exception_After_Overflow_During_A_Regular_Sync_Persists_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var firstSyncAt = world.Clock.UtcNow;
        await EstablishReconciledGenerationAsync(world, coordinator);
        world.Provider.OnPositionsRequestAsync = _ =>
        {
            world.Provider.OnPositionsRequestAsync = null;
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(40), 40)).Should().BeFalse();
            throw new HttpRequestException("exchange unavailable");
        };
        world.Clock.UtcNow = firstSyncAt.AddSeconds(1);

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.SynchronizeAsync(world.UserId, world.AccountId));

        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(firstSyncAt);
        world.Clock.UtcNow = firstSyncAt.AddSeconds(2);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(firstSyncAt.AddSeconds(2));
    }

    [Fact]
    public async Task Cancellation_After_Overflow_During_A_Regular_Sync_Persists_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await EstablishReconciledGenerationAsync(world, coordinator);
        using var cancellation = new CancellationTokenSource();
        world.Provider.OnPositionsRequestAsync = cancellationToken =>
        {
            world.Provider.OnPositionsRequestAsync = null;
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(40), 40)).Should().BeFalse();
            cancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
        };
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.SynchronizeAsync(world.UserId, world.AccountId, cancellation.Token));

        world.State!.RecoveryRequired.Should().BeTrue("the recovery write does not use the cancelled request token");
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Cancellation_Before_Rest_After_Overflow_Persists_Recovery(int cancelledStateRead)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await EstablishReconciledGenerationAsync(world, coordinator);
        RequestRecoveryByOverflow(world, coordinator);
        using var cancellation = new CancellationTokenSource();
        var stateReads = 0;
        world.BeforeStateRead = () =>
        {
            if (++stateReads == cancelledStateRead)
            {
                world.BeforeStateRead = null;
                cancellation.Cancel();
            }
        };
        var requestsBefore = world.Provider.PositionRequests.Count;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.SynchronizeAsync(world.UserId, world.AccountId, cancellation.Token));

        world.State!.RecoveryRequired.Should().BeTrue("cancellation before REST must not lose the overflow");
        world.Provider.PositionRequests.Should().HaveCount(requestsBefore);
    }

    [Fact]
    public async Task Stream_Apply_Exception_Persists_Recovery_Of_A_Reconciled_Account()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var firstSyncAt = world.Clock.UtcNow;
        await EstablishReconciledGenerationAsync(world, coordinator);
        world.BeforeTransactionExecuteAsync = _ =>
        {
            world.BeforeTransactionExecuteAsync = null;
            throw new InvalidOperationException("Injected apply failure.");
        };
        var requestsBefore = world.Provider.PositionRequests.Count;
        world.Clock.UtcNow = firstSyncAt.AddSeconds(1);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        thrown.Message.Should().Be("Injected apply failure.");
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(firstSyncAt);
        world.Provider.PositionRequests.Should().HaveCount(requestsBefore);
        world.Clock.UtcNow = firstSyncAt.AddSeconds(2);
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Recovery_Write_On_An_Exceptional_Path_Keeps_The_Original_Exception_And_The_Local_Request(
        bool conflict)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await EstablishReconciledGenerationAsync(world, coordinator);
        world.Provider.OnPositionsRequestAsync = _ =>
        {
            world.Provider.OnPositionsRequestAsync = null;
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(40), 40)).Should().BeFalse();
            if (conflict)
            {
                world.StateConflictsToInject = 3;
            }
            else
            {
                world.StateSaveFailuresToInject = 1;
            }

            throw new HttpRequestException("exchange unavailable");
        };
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinator.SynchronizeAsync(world.UserId, world.AccountId));

        thrown.Message.Should().Be("exchange unavailable");
        world.State!.RecoveryRequired.Should().BeFalse("the recovery write failed and only the local request remains");
        var recoveryFlags = new List<bool>();
        world.Provider.OnPositionsRequest = () => recoveryFlags.Add(world.State!.RecoveryRequired);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        recoveryFlags.Should().Equal(true);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Rest_Exception_After_A_Generation_Change_Does_Not_Write_Recovery_Into_Another_Generation()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await EstablishReconciledGenerationAsync(world, coordinator);
        var stateWrites = new List<RealtimeObservationState>();
        world.BeforeStateSave = stateWrites.Add;
        world.Provider.OnPositionsRequestAsync = _ =>
        {
            world.Provider.OnPositionsRequestAsync = null;
            world.AdvanceGeneration();
            coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(40), 40)).Should().BeFalse();
            throw new HttpRequestException("exchange unavailable");
        };
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.SynchronizeAsync(world.UserId, world.AccountId));

        stateWrites.Should().BeEmpty();
        world.State!.Generation.Should().Be(2);
        world.State.RecoveryRequired.Should().BeTrue();
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.State!.Generation.Should().Be(2);
        world.State.RecoveryRequired.Should().BeFalse();
        stateWrites.Should().ContainSingle().Which.Generation.Should().Be(2);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static ExchangeAccountObservationCoordinator CreateCoordinator(
        RealtimeTestWorld world,
        ServiceProvider provider,
        int bufferCapacity) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), world.Clock, bufferCapacity);

    /// <summary>
    /// Делает поколение 1 известным координатору и снимает persisted-признак recovery успешной полной
    /// сверкой. Оставшееся в буфере наблюдение не обрабатывается.
    /// </summary>
    private static async Task EstablishReconciledGenerationAsync(
        RealtimeTestWorld world,
        ExchangeAccountObservationCoordinator coordinator)
    {
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0, 1)).Should().BeTrue();
        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullReconciliationCommitted.Should().BeTrue();
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    /// <summary>Переполняет буфер ёмкостью 1, создавая локальный запрос recovery поколения 1.</summary>
    private static void RequestRecoveryByOverflow(
        RealtimeTestWorld world,
        ExchangeAccountObservationCoordinator coordinator)
    {
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(30), 30));
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(31), 31)).Should().BeFalse();
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
