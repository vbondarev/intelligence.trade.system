using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.DependencyInjection;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

/// <summary>
/// Package 1a: монотонный RecoveryRequestEpoch и epoch-aware подтверждение recovery.
/// </summary>
public sealed partial class ExchangeAccountObservationCoordinatorTests
{
    [Fact]
    public async Task H05_Cas_retry_on_confirm_does_not_ack_injected_newer_epoch()
    {
        var world = new RealtimeTestWorld(recoveryRequired: true);
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var baselineEpoch = world.State!.RecoveryRequestEpoch;
        RequestRecoveryByOverflow(world, coordinator);
        world.BeforeStateSave = state =>
        {
            if (!state.RecoveryRequired)
            {
                world.BeforeStateSave = null;
                world.InjectConcurrentRecoveryRequest();
            }
        };

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        // baseline + регистрация loss + concurrent inject во время confirm
        world.State.RecoveryRequestEpoch.Should().Be(baselineEpoch + 2);
        world.State.LastFullyReconciledAt.Should().BeNull();
        world.State.Generation.Should().Be(1);
    }

    [Fact]
    public async Task H07_New_loss_while_recovery_required_increments_epoch()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        RequestRecoveryByOverflow(world, coordinator);

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        var epochAfterFirst = world.State.RecoveryRequestEpoch;
        epochAfterFirst.Should().BeGreaterThan(0);

        RequestRecoveryByOverflow(world, coordinator);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.RecoveryRequestEpoch.Should().Be(epochAfterFirst + 1);

        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.RecoveryRequestEpoch.Should().Be(epochAfterFirst + 1);
    }

    [Fact]
    public async Task H10_Generation_advance_during_rest_keeps_new_generation_recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        await EstablishReconciledGenerationAsync(world, coordinator);
        var epochBefore = world.State!.RecoveryRequestEpoch;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.AdvanceGeneration();
        };

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        world.State!.Generation.Should().Be(2);
        world.State.RecoveryRequired.Should().BeTrue();
        world.State.RecoveryRequestEpoch.Should().Be(epochBefore + 1);
        // LastFullyReconciledAt остаётся от прежнего успешного confirm поколения 1.
        world.State.LastFullyReconciledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task H17_Retry_of_same_recovery_request_does_not_bump_epoch()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        RequestRecoveryByOverflow(world, coordinator);

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        var afterFirst = world.State!.RecoveryRequestEpoch;
        afterFirst.Should().Be(2);

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequestEpoch.Should().Be(afterFirst);
        world.State.RecoveryRequired.Should().BeTrue();

        RequestRecoveryByOverflow(world, coordinator);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequestEpoch.Should().Be(afterFirst + 1);
    }

    [Fact]
    public async Task Successful_full_sync_clears_only_matching_epoch_and_keeps_epoch_value()
    {
        var world = new RealtimeTestWorld(recoveryRequired: true);
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        world.State!.RecoveryRequestEpoch.Should().Be(1);
        world.State.RecoveryRequired.Should().BeTrue();

        await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.RecoveryRequestEpoch.Should().Be(1);
        world.State.LastFullyReconciledAt.Should().Be(world.Clock.UtcNow);
    }

    [Fact]
    public async Task Concurrent_epoch_bump_during_rest_prevents_stale_confirm()
    {
        var world = new RealtimeTestWorld(recoveryRequired: true);
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        world.State!.RecoveryRequired.Should().BeTrue();
        var epochAtStart = world.State.RecoveryRequestEpoch;
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            world.InjectConcurrentRecoveryRequest();
        };

        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);

        sync.FullReconciliationCommitted.Should().BeTrue();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.RecoveryRequestEpoch.Should().Be(epochAtStart + 1);
        world.State.LastFullyReconciledAt.Should().BeNull();
    }
}
