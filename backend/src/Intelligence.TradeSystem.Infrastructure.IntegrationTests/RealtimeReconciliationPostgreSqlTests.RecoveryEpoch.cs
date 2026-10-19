using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Package 1a: versioned recovery — конкурентное подтверждение REST не снимает более новый
/// persisted-запрос восстановления другого экземпляра.
/// </summary>
public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    /// <summary>
    /// H-02: экземпляр A фиксирует epoch до REST, экземпляр B регистрирует новый recovery во время
    /// сверки A; успешное завершение A не снимает запрос B и не обновляет LastFullyReconciledAt.
    /// </summary>
    [Fact]
    public async Task H02_Recovery_ABA_across_independent_instances_keeps_newer_recovery_request()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var providerA = new TestPrivateProvider { ObservedAt = T1.AddMinutes(1) };
        var providerB = new TestPrivateProvider { ObservedAt = T1.AddMinutes(2) };
        await using var servicesA = CreateApplicationServices(account, providerA, clock);
        await using var servicesB = CreateApplicationServices(account, providerB, clock);
        var coordinatorA = servicesA.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var coordinatorB = new ExchangeAccountObservationCoordinator(
            servicesB.GetRequiredService<IServiceScopeFactory>(),
            clock,
            bufferCapacity: 1);

        var initial = await ReadStateAsync(account);
        Assert.True(initial.RecoveryRequired);
        Assert.Equal(1, initial.RecoveryRequestEpoch);
        Assert.Null(initial.LastFullyReconciledAt);
        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);

        using var aEnteredRest = new ManualResetEventSlim(false);
        using var bCommittedRecovery = new ManualResetEventSlim(false);
        providerA.BeforePositionsResponse = () =>
        {
            providerA.BeforePositionsResponse = null;
            aEnteredRest.Set();
            Assert.True(bCommittedRecovery.Wait(CompletionTimeout));
            return Task.CompletedTask;
        };
        // B регистрирует recovery до REST и прерывает сверку, не конкурируя с commit A.
        providerB.BeforePositionsResponse = () =>
            throw new HttpRequestException("instance B aborts after recovery registration");

        var syncA = Task.Run(() => coordinatorA.SynchronizeAsync(account.UserId, account.Id));
        Assert.True(aEnteredRest.Wait(CompletionTimeout));

        Assert.True(coordinatorB.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(1), 1)));
        Assert.False(coordinatorB.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(2), 2)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinatorB.ProcessPendingAsync(account.UserId, account.Id));
        var duringARest = await ReadStateAsync(account);
        Assert.True(duringARest.RecoveryRequired);
        Assert.Equal(generation, duringARest.Generation);
        Assert.Equal(2, duringARest.RecoveryRequestEpoch);
        Assert.Null(duringARest.LastFullyReconciledAt);
        // B прервал сверку после регистрации recovery: чужих business-записей нет.
        Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(account));
        bCommittedRecovery.Set();

        var resultA = await syncA.WaitAsync(CompletionTimeout);
        Assert.True(resultA.FullReconciliationCommitted);

        var afterA = await ReadStateAsync(account);
        Assert.Equal(generation, afterA.Generation);
        Assert.True(afterA.RecoveryRequired);
        Assert.Equal(2, afterA.RecoveryRequestEpoch);
        Assert.Null(afterA.LastFullyReconciledAt);

        providerA.ObservedAt = T1.AddMinutes(3);
        Assert.True((await coordinatorA.SynchronizeAsync(account.UserId, account.Id)).FullReconciliationCommitted);
        var afterConfirm = await ReadStateAsync(account);
        Assert.False(afterConfirm.RecoveryRequired);
        Assert.Equal(2, afterConfirm.RecoveryRequestEpoch);
        Assert.Equal(T1.AddMinutes(1), afterConfirm.LastFullyReconciledAt);
    }

    /// <summary>
    /// H-05: после CAS conflict подтверждение перечитывает state и не принимает более новый epoch.
    /// </summary>
    [Fact]
    public async Task H05_Cas_conflict_on_confirm_does_not_ack_newer_recovery_epoch()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var providerA = new TestPrivateProvider { ObservedAt = T1.AddMinutes(1) };
        var providerB = new TestPrivateProvider { ObservedAt = T1.AddMinutes(2) };
        await using var servicesA = CreateApplicationServices(account, providerA, clock);
        await using var servicesB = CreateApplicationServices(account, providerB, clock);
        var coordinatorA = servicesA.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var coordinatorB = new ExchangeAccountObservationCoordinator(
            servicesB.GetRequiredService<IServiceScopeFactory>(),
            clock,
            bufferCapacity: 1);

        Assert.Equal(1, (await ReadStateAsync(account)).RecoveryRequestEpoch);

        using var aEnteredRest = new ManualResetEventSlim(false);
        using var allowAContinue = new ManualResetEventSlim(false);
        providerA.BeforePositionsResponse = () =>
        {
            providerA.BeforePositionsResponse = null;
            aEnteredRest.Set();
            Assert.True(allowAContinue.Wait(CompletionTimeout));
            return Task.CompletedTask;
        };
        providerB.BeforePositionsResponse = () =>
            throw new HttpRequestException("instance B aborts after recovery registration");

        var syncA = Task.Run(() => coordinatorA.SynchronizeAsync(account.UserId, account.Id));
        Assert.True(aEnteredRest.Wait(CompletionTimeout));

        Assert.True(coordinatorB.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(1), 1)));
        Assert.False(coordinatorB.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(2), 2)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinatorB.ProcessPendingAsync(account.UserId, account.Id));
        Assert.Equal(2, (await ReadStateAsync(account)).RecoveryRequestEpoch);
        allowAContinue.Set();

        Assert.True((await syncA.WaitAsync(CompletionTimeout)).FullReconciliationCommitted);
        var after = await ReadStateAsync(account);
        Assert.True(after.RecoveryRequired);
        Assert.Equal(2, after.RecoveryRequestEpoch);
        Assert.Null(after.LastFullyReconciledAt);
    }

    /// <summary>
    /// H-07: при уже установленном RecoveryRequired новый реальный запрос увеличивает epoch и не
    /// подтверждается текущей сверкой; следующая сверка подтверждает новый epoch.
    /// </summary>
    [Fact]
    public async Task H07_Repeated_loss_while_recovery_required_increments_epoch()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var providerA = new TestPrivateProvider { ObservedAt = T1.AddMinutes(1) };
        var providerB = new TestPrivateProvider { ObservedAt = T1.AddMinutes(2) };
        await using var servicesA = CreateApplicationServices(account, providerA, clock);
        await using var servicesB = CreateApplicationServices(account, providerB, clock);
        var coordinatorA = servicesA.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var coordinatorB = new ExchangeAccountObservationCoordinator(
            servicesB.GetRequiredService<IServiceScopeFactory>(),
            clock,
            bufferCapacity: 1);

        Assert.True((await ReadStateAsync(account)).RecoveryRequired);
        Assert.Equal(1, (await ReadStateAsync(account)).RecoveryRequestEpoch);

        using var enteredRest = new ManualResetEventSlim(false);
        using var secondLossCommitted = new ManualResetEventSlim(false);
        providerA.BeforePositionsResponse = () =>
        {
            providerA.BeforePositionsResponse = null;
            enteredRest.Set();
            Assert.True(secondLossCommitted.Wait(CompletionTimeout));
            return Task.CompletedTask;
        };
        providerB.BeforePositionsResponse = () =>
            throw new HttpRequestException("instance B aborts after recovery registration");

        var recoveryPass = Task.Run(() => coordinatorA.SynchronizeAsync(account.UserId, account.Id));
        Assert.True(enteredRest.Wait(CompletionTimeout));

        Assert.True(coordinatorB.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(1), 1)));
        Assert.False(coordinatorB.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(2), 2)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinatorB.ProcessPendingAsync(account.UserId, account.Id));
        var mid = await ReadStateAsync(account);
        Assert.True(mid.RecoveryRequired);
        Assert.Equal(2, mid.RecoveryRequestEpoch);
        secondLossCommitted.Set();

        await recoveryPass.WaitAsync(CompletionTimeout);
        var afterStaleConfirm = await ReadStateAsync(account);
        Assert.True(afterStaleConfirm.RecoveryRequired);
        Assert.Equal(2, afterStaleConfirm.RecoveryRequestEpoch);
        Assert.Null(afterStaleConfirm.LastFullyReconciledAt);

        providerA.ObservedAt = T1.AddMinutes(3);
        Assert.True((await coordinatorA.SynchronizeAsync(account.UserId, account.Id)).FullReconciliationCommitted);
        var confirmed = await ReadStateAsync(account);
        Assert.False(confirmed.RecoveryRequired);
        Assert.Equal(2, confirmed.RecoveryRequestEpoch);
    }

    /// <summary>
    /// H-10: смена generation во время подтверждения увеличивает epoch; старый sync не снимает recovery.
    /// </summary>
    [Fact]
    public async Task H10_Generation_change_during_confirm_preserves_new_generation_recovery()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var provider = new TestPrivateProvider { ObservedAt = T1.AddMinutes(1) };
        await using var services = CreateApplicationServices(account, provider, clock);
        var coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        Assert.Equal(1, (await ReadStateAsync(account)).RecoveryRequestEpoch);

        using var enteredRest = new ManualResetEventSlim(false);
        using var generationAdvanced = new ManualResetEventSlim(false);
        provider.BeforePositionsResponse = () =>
        {
            provider.BeforePositionsResponse = null;
            enteredRest.Set();
            Assert.True(generationAdvanced.Wait(CompletionTimeout));
            return Task.CompletedTask;
        };

        var sync = Task.Run(() => coordinator.SynchronizeAsync(account.UserId, account.Id));
        Assert.True(enteredRest.Wait(CompletionTimeout));

        await using (var lifecycle = fixture.CreateContext())
        {
            var advanced = await new RealtimeObservationStateStore(lifecycle).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Reconnecting);
            Assert.Equal(generation + 1, advanced.Value.Generation);
            Assert.True(advanced.Value.RecoveryRequired);
            Assert.Equal(2, advanced.Value.RecoveryRequestEpoch);
        }

        generationAdvanced.Set();
        Assert.True((await sync.WaitAsync(CompletionTimeout)).FullReconciliationCommitted);

        var after = await ReadStateAsync(account);
        Assert.Equal(generation + 1, after.Generation);
        Assert.True(after.RecoveryRequired);
        Assert.Equal(2, after.RecoveryRequestEpoch);
        Assert.Null(after.LastFullyReconciledAt);
    }

    /// <summary>
    /// H-17: два разных loss увеличивают epoch дважды; повторная запись того же запроса идемпотентна.
    /// </summary>
    [Fact]
    public async Task H17_Distinct_losses_bump_epoch_retries_are_idempotent()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var provider = new TestPrivateProvider { ObservedAt = T1.AddMinutes(1) };
        await using var services = CreateApplicationServices(account, provider, clock);
        var coordinator = new ExchangeAccountObservationCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            bufferCapacity: 1);
        provider.BeforePositionsResponse = () =>
            throw new HttpRequestException("abort after recovery registration");

        Assert.Equal(1, (await ReadStateAsync(account)).RecoveryRequestEpoch);

        Assert.True(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(1), 1)));
        Assert.False(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(2), 2)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinator.ProcessPendingAsync(account.UserId, account.Id));
        var afterLossA = await ReadStateAsync(account);
        Assert.True(afterLossA.RecoveryRequired);
        Assert.Equal(2, afterLossA.RecoveryRequestEpoch);

        // Повторная попытка сохранить тот же необработанный запрос.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinator.ProcessPendingAsync(account.UserId, account.Id));
        var afterRetry = await ReadStateAsync(account);
        Assert.True(afterRetry.RecoveryRequired);
        Assert.Equal(2, afterRetry.RecoveryRequestEpoch);

        Assert.True(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(3), 3)));
        Assert.False(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(4), 4)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinator.ProcessPendingAsync(account.UserId, account.Id));
        var afterLossB = await ReadStateAsync(account);
        Assert.True(afterLossB.RecoveryRequired);
        Assert.Equal(3, afterLossB.RecoveryRequestEpoch);

        await using var writer = fixture.CreateContext();
        var store = new RealtimeObservationStateStore(writer);
        var current = await store.GetAsync(account.UserId, account.Id);
        Assert.NotNull(current);
        await store.SaveAsync(
            account.UserId,
            current.Value with { TransportStatus = RealtimeObservationTransportStatus.Connected },
            current.Version);
        var afterCasNoise = await ReadStateAsync(account);
        Assert.Equal(3, afterCasNoise.RecoveryRequestEpoch);
        Assert.True(afterCasNoise.RecoveryRequired);
    }

    [Fact]
    public async Task Recovery_epoch_is_isolated_per_user_account()
    {
        var (accountA, generationA) = await CreateObservedAccountAsync();
        var (accountB, _) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var provider = new TestPrivateProvider { ObservedAt = T1.AddMinutes(1) };
        await using var servicesA = CreateApplicationServices(accountA, provider, clock);
        await using var servicesB = CreateApplicationServices(accountB, provider, clock);

        Assert.True((await servicesB.GetRequiredService<IExchangeAccountSyncService>()
            .SynchronizeAsync(accountB.UserId, accountB.Id)).FullReconciliationCommitted);

        var coordinatorA = new ExchangeAccountObservationCoordinator(
            servicesA.GetRequiredService<IServiceScopeFactory>(),
            clock,
            bufferCapacity: 1);
        provider.BeforePositionsResponse = () =>
            throw new HttpRequestException("abort after recovery registration");
        Assert.True(coordinatorA.TryEnqueue(
            accountA.UserId,
            generationA,
            OpenObservation(accountA, T1.AddSeconds(1), 1)));
        Assert.False(coordinatorA.TryEnqueue(
            accountA.UserId,
            generationA,
            OpenObservation(accountA, T1.AddSeconds(2), 2)));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => coordinatorA.ProcessPendingAsync(accountA.UserId, accountA.Id));

        var stateA = await ReadStateAsync(accountA);
        var stateB = await ReadStateAsync(accountB);
        Assert.True(stateA.RecoveryRequired);
        Assert.False(stateB.RecoveryRequired);
        Assert.Equal(2, stateA.RecoveryRequestEpoch);
        Assert.Equal(1, stateB.RecoveryRequestEpoch);
    }
}
