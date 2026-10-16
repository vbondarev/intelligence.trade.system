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

public sealed partial class ExchangeAccountObservationCoordinatorTests
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unconfirmed_Close_Without_Credentials_Persists_Recovery_And_Keeps_The_Channel(bool missing)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 2);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        world.Clock.UtcNow = syncAt.AddSeconds(2);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(1), 1)).Should().BeTrue();
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.CredentialsUnavailable = !missing;
        world.CredentialsMissing = missing;
        world.Clock.UtcNow = syncAt.AddSeconds(4);
        coordinator.TryEnqueue(world.UserId, 1, world.CloseObservation(syncAt.AddSeconds(3), 2)).Should().BeTrue();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode
            .Should().Be(RealtimeObservationApplyResult.RestConfirmationUnavailable);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(syncAt);
        world.ActivePositions.Should().ContainSingle();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        coordinator.ChannelCount.Should().Be(1);
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(syncAt.AddSeconds(5))).Should().BeTrue();

        world.CredentialsUnavailable = false;
        world.CredentialsMissing = false;
        world.Clock.UtcNow = syncAt.AddSeconds(10);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeFalse();
        world.ActivePositions.Should().BeEmpty();
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

    /// <summary>
    /// Помещает наблюдение кошелька в буфер именно во время <c>GetWalletBalanceAsync</c>, до ответа.
    /// </summary>
    private static void EnqueueDuringBalanceRequest(
        RealtimeTestWorld world,
        ExchangeAccountObservationCoordinator coordinator,
        PrivateWalletObservation wallet,
        DateTimeOffset balanceCompletedAt) =>
        world.Provider.OnBalanceRequest = () =>
        {
            world.Provider.OnBalanceRequest = null;
            world.Clock.UtcNow = wallet.ReceivedAt;
            coordinator.TryEnqueue(world.UserId, 1, wallet).Should().BeTrue();
            world.Clock.UtcNow = balanceCompletedAt;
        };

    /// <summary>
    /// S-23 / B-01: Failed balance + Complete positions сохраняет окно порядка; буферизованный
    /// size=2 не откатывает принятый REST size=3.
    /// </summary>
    [Fact]
    public async Task Failed_Balance_Complete_Positions_Preserves_Accepted_Positions_Rest_Window()
    {
        var world = new RealtimeTestWorld();
        world.Provider.BalanceFails = true;
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = world.Clock.UtcNow;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = restStartedAt.AddSeconds(2);
            coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(restStartedAt.AddSeconds(2), 1, OpenPosition(size: 2m)))
                .Should()
                .BeTrue();
            world.Clock.UtcNow = restStartedAt.AddSeconds(4);
        };

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.ActivePositions.Single().Size.Should().Be(3m);
        var historyAfterSync = world.ActivePositions.Single().Changes
            .Select(change => (change.Kind, change.OccurredAt))
            .ToArray();
        world.Clock.UtcNow = restStartedAt.AddSeconds(5);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.FullReconciliationCommitted.Should().BeFalse();
        sync.AcceptedPositionsObservation.Should().BeTrue();
        sync.BalanceReadStartedAt.Should().BeNull();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        var result = results.Should().ContainSingle().Subject;
        result.ConfirmedByRest.Should().BeTrue();
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.ActivePositions.Single().Changes.Select(change => (change.Kind, change.OccurredAt))
            .Should()
            .Equal(historyAfterSync);
    }

    /// <summary>
    /// S-24 / B-01: Failed balance + Partial positions сохраняет окно для реально принятых позиций.
    /// </summary>
    [Fact]
    public async Task Failed_Balance_Partial_Positions_Preserves_Order_Evidence_For_Applied_Positions()
    {
        var world = new RealtimeTestWorld();
        world.Provider.BalanceFails = true;
        world.Provider.Status = OpenPositionsObservationStatus.Partial;
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = world.Clock.UtcNow;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = restStartedAt.AddSeconds(2);
            coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(restStartedAt.AddSeconds(2), 1, OpenPosition(size: 2m)))
                .Should()
                .BeTrue();
            world.Clock.UtcNow = restStartedAt.AddSeconds(4);
        };

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var historyAfterSync = world.ActivePositions.Single().Changes
            .Select(change => (change.Kind, change.OccurredAt))
            .ToArray();
        world.Clock.UtcNow = restStartedAt.AddSeconds(5);
        // Scoped confirmation требует Complete; Status остаётся Partial → recovery без отката размера.
        world.Provider.Status = OpenPositionsObservationStatus.Partial;
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.FullReconciliationCommitted.Should().BeFalse();
        sync.AcceptedPositionsObservation.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        var result = results.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.ActivePositions.Single().Changes.Select(change => (change.Kind, change.OccurredAt))
            .Should()
            .Equal(historyAfterSync);
        world.Cursor("BTCUSDT").Should().BeNull();
        world.LatestPortfolio!.PositionsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// S-20 / B-02: успешный balance + Failed positions сохраняет окно баланса; wallet внутри
    /// интервала требует recovery без отката капитала.
    /// </summary>
    [Fact]
    public async Task Complete_Balance_Failed_Positions_Preserves_Accepted_Balance_Read_Window()
    {
        var world = new RealtimeTestWorld();
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 200m, 190m, 150m, 10m, []);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = world.Clock.UtcNow;
        var duringBalanceAt = restStartedAt.AddSeconds(1);
        var balanceCompletedAt = restStartedAt.AddSeconds(2);
        EnqueueDuringBalanceRequest(
            world,
            coordinator,
            world.WalletObservation(duringBalanceAt, totalEquity: 100m),
            balanceCompletedAt);

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var portfoliosAfterSync = world.Portfolios.Count;
        world.Clock.UtcNow = balanceCompletedAt.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.FullReconciliationCommitted.Should().BeFalse();
        sync.BalanceReadStartedAt.Should().Be(restStartedAt);
        sync.BalanceReadCompletedAt.Should().Be(balanceCompletedAt);
        world.Portfolios[portfoliosAfterSync - 1].Capital.TotalEquity.Should().Be(200m);
        world.Portfolios[portfoliosAfterSync - 1].Capital.ObservedAt.Should().Be(balanceCompletedAt);
        results.Should().Contain(result =>
            result.Outcome == RealtimeObservationApplyOutcome.RecoveryRequired &&
            result.ReasonCode == RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        world.Portfolios.Should().OnlyContain(portfolio => portfolio.Capital.TotalEquity == 200m);
        world.Portfolios.Should().NotContain(portfolio => portfolio.Capital.TotalEquity == 100m);
        world.Account.LastAppliedBalanceObservationAt.Should().BeOnOrAfter(balanceCompletedAt);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.Account.LastSyncedAt.Should().BeNull();
    }

    /// <summary>
    /// S-20 / B-02: успешный balance + Partial positions сохраняет окно баланса без полной сверки.
    /// </summary>
    [Fact]
    public async Task Complete_Balance_Partial_Positions_Preserves_Accepted_Balance_Read_Window()
    {
        var world = new RealtimeTestWorld();
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 200m, 190m, 150m, 10m, []);
        world.Provider.Status = OpenPositionsObservationStatus.Partial;
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = world.Clock.UtcNow;
        var duringBalanceAt = restStartedAt.AddSeconds(1);
        var balanceCompletedAt = restStartedAt.AddSeconds(2);
        EnqueueDuringBalanceRequest(
            world,
            coordinator,
            world.WalletObservation(duringBalanceAt, totalEquity: 100m),
            balanceCompletedAt);

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = balanceCompletedAt.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.FullReconciliationCommitted.Should().BeFalse();
        sync.AcceptedPositionsObservation.Should().BeTrue();
        sync.BalanceReadStartedAt.Should().Be(restStartedAt);
        sync.BalanceReadCompletedAt.Should().Be(balanceCompletedAt);
        results.Should().ContainSingle().Which.Outcome
            .Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(200m);
        world.LatestPortfolio!.PositionsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// B-01/B-02: проигранный commit не сохраняет капитал и watermark; evidence окон не возвращается.
    /// </summary>
    [Fact]
    public async Task Rolled_Back_Degraded_Sync_Does_Not_Record_Accepted_Rest_Windows()
    {
        var world = new RealtimeTestWorld();
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 200m, 190m, 150m, 10m, []);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.CommitFailuresToInject = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId));

        world.Portfolios.Should().BeEmpty();
        world.Account.LastAppliedBalanceObservationAt.Should().BeNull();
        world.Account.LastAppliedPositionsObservationAt.Should().BeNull();
        world.Events.Should().BeEmpty();
        world.ActivePositions.Should().BeEmpty();
    }

    /// <summary>S-20: wallet во время REST-чтения баланса не откатывает принятый капитал.</summary>
    [Fact]
    public async Task Wallet_During_Successful_Balance_Read_Requires_Recovery_Without_Capital_Regression()
    {
        var world = new RealtimeTestWorld();
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 200m, 190m, 150m, 10m, []);
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = world.Clock.UtcNow;
        var duringBalanceAt = restStartedAt.AddSeconds(1);
        var balanceCompletedAt = restStartedAt.AddSeconds(2);
        EnqueueDuringBalanceRequest(
            world,
            coordinator,
            world.WalletObservation(duringBalanceAt, totalEquity: 100m),
            balanceCompletedAt);

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var portfoliosAfterSync = world.Portfolios.Count;
        var eventsAfterSync = world.Events.Count;
        world.Clock.UtcNow = balanceCompletedAt.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        sync.FullReconciliationCommitted.Should().BeTrue();
        world.Portfolios[portfoliosAfterSync - 1].Capital.TotalEquity.Should().Be(200m);
        world.Portfolios[portfoliosAfterSync - 1].Capital.ObservedAt.Should().Be(balanceCompletedAt);
        var result = results.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        world.Portfolios.Should().OnlyContain(portfolio => portfolio.Capital.TotalEquity == 200m);
        world.Portfolios.Should().NotContain(portfolio => portfolio.Capital.TotalEquity == 100m);
        world.Account.LastAppliedBalanceObservationAt.Should().BeOnOrAfter(balanceCompletedAt);
        world.Events.Count.Should().BeGreaterThanOrEqualTo(eventsAfterSync);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    /// <summary>S-20 / Failed REST: неуспешное чтение баланса не запрещает допустимый wallet.</summary>
    [Fact]
    public async Task Wallet_During_Failed_Balance_Read_Is_Still_Applied()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var failedAt = T0.AddMinutes(2);
        world.Clock.UtcNow = failedAt;
        world.Provider.BalanceFails = true;
        var duringBalanceAt = failedAt.AddSeconds(1);
        EnqueueDuringBalanceRequest(
            world,
            coordinator,
            world.WalletObservation(duringBalanceAt, totalEquity: 1_100m),
            balanceCompletedAt: failedAt.AddSeconds(2));

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = failedAt.AddSeconds(3);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_100m);
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(duringBalanceAt);
    }

    /// <summary>
    /// R-208-03: Outcome=Synchronized при AcceptedPositionsObservation=false не создаёт FullSyncWindow;
    /// допустимое потоковое обновление внутри интервала запроса применяется без scoped confirmation.
    /// </summary>
    [Fact]
    public async Task Synchronized_Balance_With_Superseded_Failed_Positions_Does_Not_Create_Rest_Window()
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        var bootstrap = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        bootstrap.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        bootstrap.AcceptedPositionsObservation.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.ActivePositions.Single().LastObservedAt.Should().Be(T0.AddSeconds(10));
        world.SetPositionsWatermark(T0.AddSeconds(15));
        world.LatestPortfolio!.PositionsFullyReconciled.Should().BeTrue();

        world.Provider.Balance = new AccountBalance(AccountType.Unified, 2_100m, 2_000m, 1_600m, 100m, []);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.Provider.SnapshotObservedAt = T0.AddSeconds(14);
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = T0.AddSeconds(20);
        world.Clock.UtcNow = restStartedAt;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = T0.AddSeconds(21);
            coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(T0.AddSeconds(21), 2, OpenPosition(size: 2m)))
                .Should()
                .BeTrue();
            world.Clock.UtcNow = T0.AddSeconds(22);
        };

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        sync.AcceptedPositionsObservation.Should().BeFalse();
        sync.FullReconciliationCommitted.Should().BeFalse();
        world.ActivePositions.Single().Size.Should().Be(1m);

        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Provider.SnapshotObservedAt = null;
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        var requestsBeforePending = world.Provider.PositionRequests.Count;
        world.Clock.UtcNow = T0.AddSeconds(23);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var applied = results.Should().ContainSingle().Subject;
        applied.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        applied.ConfirmedByRest.Should().BeFalse();
        world.Provider.PositionRequests.Count.Should().Be(requestsBeforePending);
        world.Provider.PositionRequests.Should().OnlyContain(symbol => symbol == null);
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.Size.Should().Be(2m);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.LastObservedAt.Should().Be(T0.AddSeconds(21));
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
    }

    /// <summary>
    /// R-208-03: непринятые позиции при Synchronized не расширяют прежнее успешное REST-окно
    /// и не уничтожают его доказательство порядка.
    /// </summary>
    [Fact]
    public async Task Synchronized_Without_Accepted_Positions_Does_Not_Extend_Previous_Rest_Window()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var firstSyncAt = T0.AddSeconds(10);
        world.Clock.UtcNow = firstSyncAt;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = firstSyncAt.AddSeconds(1);
        };

        var first = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        first.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        first.AcceptedPositionsObservation.Should().BeTrue();
        first.FullReconciliationCommitted.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.State!.RecoveryRequired.Should().BeFalse();

        world.SetPositionsWatermark(T0.AddSeconds(15));
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 2_100m, 2_000m, 1_600m, 100m, []);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.Provider.SnapshotObservedAt = T0.AddSeconds(14);
        world.Clock.UtcNow = T0.AddSeconds(20);
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = T0.AddSeconds(22);
        };

        var second = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        second.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        second.AcceptedPositionsObservation.Should().BeFalse();
        second.FullReconciliationCommitted.Should().BeFalse();

        // Интервал второй сверки не должен входить в сохранённое окно позиций.
        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Provider.SnapshotObservedAt = null;
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        var requestsBeforeOutside = world.Provider.PositionRequests.Count;
        world.Clock.UtcNow = T0.AddSeconds(23);
        coordinator.TryEnqueue(
                world.UserId,
                1,
                world.OpenObservation(T0.AddSeconds(21), 2, OpenPosition(size: 2m)))
            .Should()
            .BeTrue();
        var outside = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var appliedOutside = outside.Should().ContainSingle().Subject;
        appliedOutside.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        appliedOutside.ConfirmedByRest.Should().BeFalse();
        world.Provider.PositionRequests.Count.Should().Be(requestsBeforeOutside);
        world.ActivePositions.Single().Size.Should().Be(2m);

        // Первое окно сохранено: пересекающее его наблюдение всё ещё идёт в scoped confirmation.
        world.CredentialsUnavailable = true;
        coordinator.TryEnqueue(
                world.UserId,
                1,
                world.OpenObservation(firstSyncAt.AddMilliseconds(500), 3, OpenPosition(size: 1m)))
            .Should()
            .BeTrue();
        var overlapping = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var blocked = overlapping.Should().ContainSingle().Subject;
        blocked.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        blocked.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationUnavailable);
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
    }

    /// <summary>
    /// R-208-01: Failed positions при Applied watermark не создаёт окно REST; более новая
    /// допустимая revision применяется напрямую без scoped confirmation.
    /// </summary>
    [Fact]
    public async Task Failed_Positions_Does_Not_Create_Rest_Window_Or_Force_Scoped_Confirmation()
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(10);
        await world.CreatePositionService().ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 1));
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var restStartedAt = T0.AddSeconds(11);
        world.Clock.UtcNow = restStartedAt;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            // Интервал запроса [T11, T14]: ObservedAt снимка зафиксирован до колбэка как T11.
            world.Clock.UtcNow = restStartedAt.AddSeconds(3);
        };

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.AcceptedPositionsObservation.Should().BeFalse();
        sync.FullReconciliationCommitted.Should().BeFalse();
        world.ActivePositions.Single().TrackingState.Should().Be(PositionTrackingState.Unknown);
        world.ActivePositions.Single().LastObservedAt.Should().Be(T0.AddSeconds(10));
        world.ActivePositions.Single().Changes[^1].OccurredAt.Should().Be(restStartedAt);

        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Provider.RemoveExchangePosition("BTCUSDT");
        world.Clock.UtcNow = restStartedAt.AddSeconds(4);
        coordinator.TryEnqueue(
                world.UserId,
                1,
                world.OpenObservation(T0.AddSeconds(12), 2, OpenPosition(size: 2m)))
            .Should()
            .BeTrue();
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var applied = results.Should().ContainSingle().Subject;
        applied.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        applied.ConfirmedByRest.Should().BeFalse();
        world.Provider.PositionRequests.Should().Equal([null]);
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.Size.Should().Be(2m);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.LastObservedAt.Should().Be(T0.AddSeconds(12));
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
    }

    /// <summary>
    /// R-208-01: Failed positions не уничтожает ранее принятое успешное окно порядка.
    /// </summary>
    [Fact]
    public async Task Failed_Positions_Preserves_Previous_Successful_Rest_Window()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var firstSyncAt = world.Clock.UtcNow;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = firstSyncAt.AddSeconds(1);
            coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(firstSyncAt.AddSeconds(1), 1, OpenPosition(size: 2m)))
                .Should()
                .BeTrue();
            world.Clock.UtcNow = firstSyncAt.AddSeconds(2);
        };

        var first = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        first.AcceptedPositionsObservation.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(3m);

        world.Clock.UtcNow = firstSyncAt.AddSeconds(5);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        var failed = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        failed.AcceptedPositionsObservation.Should().BeFalse();
        failed.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);

        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Clock.UtcNow = firstSyncAt.AddSeconds(6);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
    }

    /// <summary>
    /// R-208-02 / S-27: Complete REST с равным временем и противоречащим снимком не снимает recovery.
    /// </summary>
    [Fact]
    public async Task Equal_Timestamp_Conflicting_Complete_Rest_Does_Not_Clear_Recovery()
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(10);
        await world.CreatePositionService().ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(10), 1, OpenPosition(size: 1m)));
        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        sync.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        sync.FullReconciliationCommitted.Should().BeFalse();
        sync.AcceptedPositionsObservation.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.LatestPortfolio!.PositionsFullyReconciled.Should().BeFalse();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().BeNull();
    }

    /// <summary>S-24: неуспешная сверка после успешной не уничтожает доказательство порядка для буфера.</summary>
    [Fact]
    public async Task Failed_Sync_After_Successful_Does_Not_Allow_Buffered_Position_Regression()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var firstSyncAt = world.Clock.UtcNow;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = firstSyncAt.AddSeconds(1);
            coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(firstSyncAt.AddSeconds(1), 1, OpenPosition(size: 2m)))
                .Should()
                .BeTrue();
            world.Clock.UtcNow = firstSyncAt.AddSeconds(2);
        };

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.ActivePositions.Single().Size.Should().Be(3m);

        world.Clock.UtcNow = firstSyncAt.AddSeconds(5);
        world.CredentialsUnavailable = true;
        var failed = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        failed.Outcome.Should().Be(ExchangeAccountSyncOutcome.CredentialsUnavailable);
        world.CredentialsUnavailable = false;
        world.Clock.UtcNow = firstSyncAt.AddSeconds(6);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.ActivePositions.Single().Changes.Should().ContainSingle();
    }

    /// <summary>S-25: последовательные scoped REST-окна объединяются и не теряют доказательство порядка.</summary>
    [Fact]
    public async Task Sequential_Scoped_Rest_Windows_Keep_Order_Evidence_For_Delayed_Observation()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        world.SetPositionsWatermark(T0.AddSeconds(10));
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Clock.UtcNow = T0.AddSeconds(11);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(8), 1)).Should().BeTrue();
        var first = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        first.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        var firstScopedAt = T0.AddSeconds(11);

        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(20);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(9), 2, OpenPosition(size: 2m)))
            .Should()
            .BeTrue();
        var second = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        second.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(2m);

        // ObservedAt только в первом scoped-окне: без merge окно заменилось бы вторым подтверждением,
        // и более новая ревизия напрямую записала бы size=1 поверх size=2.
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(21);
        coordinator.TryEnqueue(
                world.UserId,
                1,
                world.OpenObservation(firstScopedAt, 3, OpenPosition(size: 1m)))
            .Should()
            .BeTrue();
        var delayed = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        delayed.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
    }

    /// <summary>S-28: откат часов между началом и завершением REST сохраняет покрытие окна.</summary>
    [Fact]
    public async Task Rest_Window_With_Clock_Rollback_Still_Forces_Confirmation_For_Overlapping_Position()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var startedAt = world.Clock.UtcNow;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.Clock.UtcNow = startedAt.AddSeconds(-5);
            coordinator.TryEnqueue(
                    world.UserId,
                    1,
                    world.OpenObservation(startedAt.AddSeconds(-2), 1, OpenPosition(size: 2m)))
                .Should()
                .BeTrue();
        };

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        world.ActivePositions.Single().Size.Should().Be(3m);
        world.Clock.UtcNow = startedAt.AddSeconds(1);
        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
    }

    /// <summary>
    /// R-208-04 / G1: задержанное открытие неизвестного hedge-slot после пустого Complete scoped REST
    /// не создаёт ложную позицию.
    /// </summary>
    [Fact]
    public async Task Delayed_Open_Of_Unknown_Hedge_Slot_Before_Scoped_Rest_Window_Does_Not_Create_Position()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        world.SetPositionsWatermark(T0);
        world.Clock.UtcNow = T0.AddSeconds(5);
        var longHedge = OpenPosition(side: PositionSide.Long, positionIdx: 1);
        await world.CreatePositionService()
            .ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(5), 1, longHedge));
        world.ActivePositions.Should().ContainSingle()
            .Which.ExchangePositionKey.PositionIdx.Should().Be(1);

        world.Provider.RemoveExchangePosition("BTCUSDT");
        world.Clock.UtcNow = T0.AddSeconds(10);
        coordinator.TryEnqueue(world.UserId, 1, world.CloseObservation(T0.AddSeconds(9), 2, positionIdx: 1))
            .Should()
            .BeTrue();
        var closeResults = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        closeResults.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().BeEmpty();
        var scopedWindowEnd = world.Clock.UtcNow;

        var shortHedge = OpenPosition(side: PositionSide.Short, positionIdx: 2);
        world.Provider.RemoveExchangePosition("BTCUSDT");
        world.Clock.UtcNow = scopedWindowEnd.AddSeconds(1);
        var delayedOpen = world.OpenObservation(T0.AddSeconds(9), 3, shortHedge) with
        {
            ReceivedAt = world.Clock.UtcNow,
        };
        coordinator.TryEnqueue(world.UserId, 1, delayedOpen).Should().BeTrue();
        var delayed = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        delayed.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().BeEmpty();
        world.Events.OfType<PositionOpenedEventV1>().Should().HaveCount(1);
        world.Positions.Should().ContainSingle()
            .Which.TrackingState.Should().Be(PositionTrackingState.Closed);
    }

    /// <summary>R-208-04: наблюдение до scoped-окна не откатывает уже подтверждённый размер.</summary>
    [Fact]
    public async Task Delayed_Open_Before_Scoped_Rest_Window_Does_Not_Regress_Confirmed_Size()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        world.SetPositionsWatermark(T0.AddSeconds(10));
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Clock.UtcNow = T0.AddSeconds(11);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(8), 1)).Should().BeTrue();
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Single().Size.Should().Be(1m);
        var firstScopedAt = T0.AddSeconds(11);

        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Clock.UtcNow = T0.AddSeconds(20);
        coordinator.TryEnqueue(
                world.UserId,
                1,
                world.OpenObservation(firstScopedAt.AddSeconds(-2), 2, OpenPosition(size: 2m)))
            .Should()
            .BeTrue();
        var delayed = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        delayed.Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(1m);
    }

    /// <summary>R-208-04: Failed scoped REST не создаёт SymbolWindow (ConfirmedByRest=false).</summary>
    [Fact]
    public async Task Failed_Scoped_Confirmation_Does_Not_Create_Refuting_Symbol_Window()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        world.SetPositionsWatermark(T0.AddSeconds(10));
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.Clock.UtcNow = T0.AddSeconds(11);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(8), 1)).Should().BeTrue();
        var failed = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        var result = failed.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ConfirmedByRest.Should().BeFalse();
        world.Positions.Should().BeEmpty();
        world.Cursor("BTCUSDT").Should().BeNull();
    }

    /// <summary>R-208-04: отдельный символ не наследует чужое scoped-подтверждение.</summary>
    [Fact]
    public async Task Scoped_Rest_Window_Does_Not_Apply_To_Other_Symbol()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        world.SetPositionsWatermark(T0.AddSeconds(10));
        world.Provider.SetExchangePosition(OpenPosition("BTCUSDT", size: 1m));
        world.Clock.UtcNow = T0.AddSeconds(11);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(T0.AddSeconds(8), 1)).Should().BeTrue();
        (await coordinator.ProcessPendingAsync(world.UserId, world.AccountId))
            .Should().ContainSingle().Which.ConfirmedByRest.Should().BeTrue();

        world.Provider.SetExchangePosition(OpenPosition("ETHUSDT", size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(12);
        var ethOpen = world.OpenObservation(T0.AddSeconds(11).AddMilliseconds(500), 1, OpenPosition("ETHUSDT", size: 2m));
        coordinator.TryEnqueue(world.UserId, 1, ethOpen).Should().BeTrue();
        var eth = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        eth.Should().ContainSingle().Which.ConfirmedByRest.Should().BeFalse();
        world.ActivePositions.Should().HaveCount(2);
    }

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
