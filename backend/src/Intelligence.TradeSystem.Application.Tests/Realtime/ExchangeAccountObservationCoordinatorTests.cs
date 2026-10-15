using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
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
    public async Task Wallet_Observation_Not_Newer_Than_Full_Sync_Is_Not_Applied()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = provider.GetRequiredService<ExchangeAccountObservationCoordinator>();
        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        var portfoliosAfterSync = world.Portfolios.Count;

        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(world.Clock.UtcNow)).Should().BeTrue();
        var overlapping = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(30);
        coordinator.TryEnqueue(world.UserId, 1, world.WalletObservation(world.Clock.UtcNow.AddSeconds(-1)))
            .Should()
            .BeTrue();
        var newer = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        overlapping.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.OverlapsRestObservation);
        newer.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Portfolios.Should().HaveCount(portfoliosAfterSync + 1);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_000m);
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
