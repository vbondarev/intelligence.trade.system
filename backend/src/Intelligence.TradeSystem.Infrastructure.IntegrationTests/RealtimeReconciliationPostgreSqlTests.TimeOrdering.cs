using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Временная согласованность потоковых и REST-наблюдений при откате часов backend и восстановление
/// признака recovery при исключениях. Каждое утверждение проверяется повторным чтением новым DbContext.
/// </summary>
public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    [Fact]
    public async Task Delayed_update_older_than_unknown_marking_requires_recovery_without_writes()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, (await ApplyAsync(account, generation, OpenObservation(account, T1, 10))).Outcome);
        var failedSync = await SynchronizeAsync(
            account,
            new TestPrivateProvider { ObservedAt = T1.AddSeconds(10), Status = OpenPositionsObservationStatus.Failed },
            new FixedTimeProvider(T1.AddMinutes(1)));
        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, failedSync.Outcome);
        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);

        var delayed = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(5), 11, OpenPosition(size: 2m)));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, delayed.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.OlderThanPositionHistory, delayed.ReasonCode);
        await using (var verification = fixture.CreateContext())
        {
            var position = await SinglePositionAsync(verification, account);
            Assert.Equal(PositionTrackingState.Unknown, position.TrackingState);
            Assert.Equal(1m, position.Size);
            AssertHistory(position, (PositionChangeKind.New, T1), (PositionChangeKind.MarkedUnknown, T1.AddSeconds(10)));
            Assert.Equal(10, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
            Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
        }

        var recovered = await ApplyAsync(account, generation, OpenObservation(account, T1.AddSeconds(11), 12));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, recovered.Outcome);
        await using var reread = fixture.CreateContext();
        var recoveredPosition = await SinglePositionAsync(reread, account);
        Assert.Equal(PositionTrackingState.Active, recoveredPosition.TrackingState);
        AssertHistory(
            recoveredPosition,
            (PositionChangeKind.New, T1),
            (PositionChangeKind.MarkedUnknown, T1.AddSeconds(10)),
            (PositionChangeKind.Recovered, T1.AddSeconds(11)));
    }

    [Fact]
    public async Task Scoped_rest_older_than_stale_marking_after_a_clock_rollback_does_not_rewrite_history()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await ApplyAsync(account, generation, OpenObservation(account, T1, 10));
        var staleAt = T1.AddMinutes(10);
        var ethProvider = new TestPrivateProvider { ObservedAt = staleAt };
        ethProvider.SetPosition(OpenPosition("ETHUSDT"));
        var eth = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddMinutes(9), 1, OpenPosition("ETHUSDT")),
            ethProvider,
            new FixedTimeProvider(staleAt),
            confirmWithRest: true);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, eth.Outcome);
        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);

        var rolledBack = new FixedTimeProvider(T1.AddMinutes(6));
        var close = await ApplyAsync(
            account,
            generation,
            CloseObservation(account, T1.AddMinutes(5), 11),
            new TestPrivateProvider { ObservedAt = T1.AddMinutes(6) },
            rolledBack);

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, close.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RestConfirmationIncomplete, close.ReasonCode);
        await using var verification = fixture.CreateContext();
        var btc = (await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id))
            .Select(versioned => versioned.Value)
            .Single(position => position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT");
        Assert.Equal(PositionTrackingState.Stale, btc.TrackingState);
        AssertHistory(btc, (PositionChangeKind.New, T1), (PositionChangeKind.MarkedStale, staleAt));
        Assert.Equal(10, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
        Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
    }

    [Fact]
    public async Task Rest_snapshot_after_a_newer_stream_snapshot_becomes_the_latest_portfolio()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await ApplyAsync(account, generation, OpenObservation(account, T1.AddSeconds(10), 1), clock: new FixedTimeProvider(T1.AddSeconds(100)));
        var streamSnapshot = await LatestPortfolioAsync(account);
        Assert.Equal(T1.AddSeconds(100), streamSnapshot.CalculatedAt);
        var rest = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        rest.SetPosition(OpenPosition(size: 2m));

        var result = await SynchronizeAsync(account, rest, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, result.Outcome);
        Assert.True(result.FullReconciliationCommitted);
        var latest = await LatestPortfolioAsync(account);
        Assert.Equal(T1.AddSeconds(100).AddTicks(TimeSpan.TicksPerMicrosecond), latest.CalculatedAt);
        Assert.Equal(result.PortfolioState!.CalculatedAt, latest.CalculatedAt);
        Assert.Equal(T1.AddSeconds(20), latest.Capital.ObservedAt);
        Assert.Equal(2m, Assert.Single(latest.Positions).Size);
        await using var verification = fixture.CreateContext();
        Assert.Equal(2, await CountPortfoliosAsync(verification, account));
        Assert.Equal(T1.AddSeconds(20), (await SinglePositionAsync(verification, account)).LastObservedAt);
    }

    [Fact]
    public async Task Rest_older_than_a_stream_fact_after_a_clock_rollback_keeps_the_fact_and_orders_the_snapshot()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await ApplyAsync(account, generation, OpenObservation(account, T1.AddSeconds(100), 1), clock: new FixedTimeProvider(T1.AddSeconds(101)));
        var streamSnapshot = await LatestPortfolioAsync(account);
        var rest = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        rest.SetPosition(OpenPosition(size: 2m));

        var result = await SynchronizeAsync(account, rest, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, result.Outcome);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(1m, position.Size);
        Assert.Equal(T1.AddSeconds(100), position.LastObservedAt);
        AssertHistory(position, (PositionChangeKind.New, T1.AddSeconds(100)));
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.True(latest!.CalculatedAt > streamSnapshot.CalculatedAt);
        Assert.Equal(T1.AddSeconds(20), latest.Capital.ObservedAt);
        Assert.Equal(1m, Assert.Single(latest.Positions).Size);
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(20), stored!.Value.LastAppliedPositionsObservationAt);
        Assert.Equal("positions_ambiguous", stored.Value.LastError);
    }

    [Fact]
    public async Task Startup_and_reconnect_syncs_without_stream_observations_reconcile_the_persisted_generation()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider();
        await using var services = CreateApplicationServices(account, provider);
        var coordinator = services.GetRequiredService<IExchangeAccountSyncService>();

        var startup = await coordinator.SynchronizeAsync(account.UserId, account.Id);

        Assert.True(startup.FullReconciliationCommitted);
        var reconciled = await ReadStateAsync(account);
        Assert.Equal(generation, reconciled.Generation);
        Assert.False(reconciled.RecoveryRequired);
        Assert.Equal(T1.AddMinutes(1), reconciled.LastFullyReconciledAt);

        provider.ObservedAt = T1.AddSeconds(1);
        provider.BeforePositionsResponse = async () =>
        {
            provider.BeforePositionsResponse = null;
            await using var context = fixture.CreateContext();
            await new RealtimeObservationStateStore(context).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Connected);
        };
        var duringReconnect = await coordinator.SynchronizeAsync(account.UserId, account.Id);

        Assert.True(duringReconnect.FullReconciliationCommitted);
        var reconnected = await ReadStateAsync(account);
        Assert.Equal(generation + 1, reconnected.Generation);
        Assert.True(reconnected.RecoveryRequired);

        provider.ObservedAt = T1.AddSeconds(2);
        await coordinator.SynchronizeAsync(account.UserId, account.Id);

        var afterReconnect = await ReadStateAsync(account);
        Assert.Equal(generation + 1, afterReconnect.Generation);
        Assert.False(afterReconnect.RecoveryRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overflow_followed_by_rest_failure_or_cancellation_persists_recovery(bool cancel)
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider();
        await using var services = CreateApplicationServices(account, provider);
        var coordinator = new ExchangeAccountObservationCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(T1.AddMinutes(1)),
            bufferCapacity: 1);
        Assert.True(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, T1, 1)));
        Assert.True((await coordinator.SynchronizeAsync(account.UserId, account.Id)).FullReconciliationCommitted);
        Assert.False((await ReadStateAsync(account)).RecoveryRequired);
        using var cancellation = new CancellationTokenSource();
        provider.ObservedAt = T1.AddSeconds(1);
        provider.BeforePositionsResponse = () =>
        {
            provider.BeforePositionsResponse = null;
            Assert.False(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, T1.AddSeconds(1), 2)));
            if (!cancel)
            {
                throw new HttpRequestException("exchange unavailable");
            }

            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => coordinator.SynchronizeAsync(account.UserId, account.Id, cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.SynchronizeAsync(account.UserId, account.Id));
        }

        Assert.True((await ReadStateAsync(account)).RecoveryRequired);
        provider.ObservedAt = T1.AddSeconds(2);
        var recovery = await coordinator.ProcessPendingAsync(account.UserId, account.Id);
        Assert.Equal(RealtimeObservationApplyResult.BufferOverflow, Assert.Single(recovery).ReasonCode);
        Assert.False((await ReadStateAsync(account)).RecoveryRequired);
    }

    [Fact]
    public async Task Recovery_required_by_the_last_observation_of_a_bounded_pass_survives_a_coordinator_restart()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider();
        await using var services = CreateApplicationServices(account, provider);
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var coordinator = new ExchangeAccountObservationCoordinator(
            scopeFactory,
            new FixedTimeProvider(T1.AddMinutes(1)),
            bufferCapacity: 1);
        Assert.True((await coordinator.SynchronizeAsync(account.UserId, account.Id)).FullReconciliationCommitted);
        var reconciled = await ReadStateAsync(account);
        Assert.False(reconciled.RecoveryRequired);
        Assert.Equal(T1.AddMinutes(1), reconciled.LastFullyReconciledAt);
        var openAt = T1.AddMinutes(2);
        Assert.True(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, openAt, 10)));
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            Assert.Single(await coordinator.ProcessPendingAsync(account.UserId, account.Id)).Outcome);
        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);

        Assert.True(coordinator.TryEnqueue(
            account.UserId,
            generation,
            OpenObservation(account, openAt, 10, OpenPosition(size: 2m))));
        var conflict = Assert.Single(await coordinator.ProcessPendingAsync(account.UserId, account.Id));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, conflict.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RevisionConflict, conflict.ReasonCode);
        await using (var verification = fixture.CreateContext())
        {
            var persisted = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
            Assert.Equal(generation, persisted!.Value.Generation);
            Assert.True(persisted.Value.RecoveryRequired);
            Assert.Equal(reconciled.LastFullyReconciledAt, persisted.Value.LastFullyReconciledAt);
            var position = await SinglePositionAsync(verification, account);
            Assert.Equal(PositionTrackingState.Active, position.TrackingState);
            Assert.Equal(1m, position.Size);
            AssertHistory(position, (PositionChangeKind.New, openAt));
            Assert.Equal(10, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
            Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
        }

        var restarted = new ExchangeAccountObservationCoordinator(
            scopeFactory,
            new FixedTimeProvider(T1.AddMinutes(3)),
            bufferCapacity: 1);
        Assert.Empty(await restarted.ProcessPendingAsync(account.UserId, account.Id));
        Assert.True((await ReadStateAsync(account)).RecoveryRequired);
        provider.ObservedAt = T1.AddMinutes(3);
        provider.SetPosition(OpenPosition(size: 2m));

        var recovery = await restarted.SynchronizeAsync(account.UserId, account.Id);

        Assert.True(recovery.FullReconciliationCommitted);
        var recovered = await ReadStateAsync(account);
        Assert.Equal(generation, recovered.Generation);
        Assert.False(recovered.RecoveryRequired);
        Assert.Equal(T1.AddMinutes(3), recovered.LastFullyReconciledAt);
        await using var reread = fixture.CreateContext();
        Assert.Equal(2m, (await SinglePositionAsync(reread, account)).Size);
    }

    [Fact]
    public async Task Closed_lifecycles_observed_later_do_not_break_scoped_confirmation_of_another_symbol()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var closedBtc = Position.Create(
            ExchangePositionKey.Create(account.Id, InstrumentId.From("BTCUSDT"), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            T1.AddSeconds(8),
            T1.AddSeconds(10),
            averageEntryPrice: 100m,
            leverage: 2m);
        closedBtc.Close(T1.AddSeconds(10), PositionChangeCause.MissingFromCompleteObservation);
        var eth = Position.Create(
            ExchangePositionKey.Create(account.Id, InstrumentId.From("ETHUSDT"), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            T1.AddSeconds(1),
            T1.AddSeconds(1),
            averageEntryPrice: 100m,
            leverage: 2m);
        await SeedPositionsAsync(account, closedBtc, eth);
        var rest = new TestPrivateProvider { ObservedAt = T1.AddSeconds(6) };
        rest.SetPosition(OpenPosition("ETHUSDT", size: 2m));

        var result = await ApplyAsync(
            account,
            generation,
            CloseObservation(account, T1.AddSeconds(2), 1, "ETHUSDT"),
            rest,
            new FixedTimeProvider(T1.AddSeconds(6)));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        Assert.True(result.ConfirmedByRest);
        await using var verification = fixture.CreateContext();
        var stored = (await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id))
            .Select(versioned => versioned.Value)
            .ToDictionary(position => position.Id);
        var storedBtc = stored[closedBtc.Id];
        Assert.Equal(PositionTrackingState.Closed, storedBtc.TrackingState);
        AssertHistory(storedBtc, (PositionChangeKind.New, T1.AddSeconds(10)), (PositionChangeKind.Closed, T1.AddSeconds(10)));
        var storedEth = stored[eth.Id];
        Assert.Equal(2m, storedEth.Size);
        Assert.Equal(T1.AddSeconds(6), storedEth.LastObservedAt);
        Assert.Equal(1, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account, "ETHUSDT")))!.Value.LastProviderSequence);
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(2m, Assert.Single(latest!.Positions).Size);
    }

    [Fact]
    public async Task Clock_rollback_with_closed_lifecycles_keeps_history_snapshots_and_recovery_consistent()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var closedSol = Position.Create(
            ExchangePositionKey.Create(account.Id, InstrumentId.From("SOLUSDT"), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            T1.AddMinutes(30),
            T1.AddMinutes(30),
            averageEntryPrice: 100m,
            leverage: 2m);
        closedSol.Close(T1.AddMinutes(30), PositionChangeCause.MissingFromCompleteObservation);
        await SeedPositionsAsync(account, closedSol);
        await ApplyAsync(account, generation, OpenObservation(account, T1.AddSeconds(10), 1), clock: new FixedTimeProvider(T1.AddSeconds(100)));
        var streamSnapshot = await LatestPortfolioAsync(account);

        var failed = await SynchronizeAsync(
            account,
            new TestPrivateProvider { ObservedAt = T1.AddSeconds(20), Status = OpenPositionsObservationStatus.Failed },
            new FixedTimeProvider(T1.AddSeconds(20)));
        var delayed = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(15), 2, OpenPosition(size: 2m)),
            clock: new FixedTimeProvider(T1.AddSeconds(21)));

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, failed.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.OlderThanPositionHistory, delayed.ReasonCode);
        await using (var verification = fixture.CreateContext())
        {
            var positions = (await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id))
                .Select(versioned => versioned.Value)
                .ToDictionary(position => position.ExchangePositionKey.InstrumentId.Value);
            AssertHistory(positions["BTCUSDT"], (PositionChangeKind.New, T1.AddSeconds(10)), (PositionChangeKind.MarkedUnknown, T1.AddSeconds(20)));
            AssertHistory(positions["SOLUSDT"], (PositionChangeKind.New, T1.AddMinutes(30)), (PositionChangeKind.Closed, T1.AddMinutes(30)));
            var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
            Assert.Equal(streamSnapshot.CalculatedAt.AddTicks(TimeSpan.TicksPerMicrosecond), latest!.CalculatedAt);
            Assert.Equal(T1.AddSeconds(20), latest.Capital.ObservedAt);
            Assert.Equal(1, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
        }

        var recovery = new TestPrivateProvider { ObservedAt = T1.AddSeconds(40) };
        recovery.SetPosition(OpenPosition());
        var recovered = await SynchronizeAsync(account, recovery, new FixedTimeProvider(T1.AddSeconds(40)));

        Assert.True(recovered.FullReconciliationCommitted);
        await using var reread = fixture.CreateContext();
        var btc = (await new PositionRepository(reread).GetByExchangeAccountAsync(account.UserId, account.Id))
            .Select(versioned => versioned.Value)
            .Single(position => position.TrackingState != PositionTrackingState.Closed);
        AssertHistory(
            btc,
            (PositionChangeKind.New, T1.AddSeconds(10)),
            (PositionChangeKind.MarkedUnknown, T1.AddSeconds(20)),
            (PositionChangeKind.Recovered, T1.AddSeconds(40)));
        var finalSnapshot = await new PortfolioStateRepository(reread).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(streamSnapshot.CalculatedAt.AddTicks(2 * TimeSpan.TicksPerMicrosecond), finalSnapshot!.CalculatedAt);
        Assert.Equal(recovered.PortfolioState!.CalculatedAt, finalSnapshot.CalculatedAt);
        Assert.Equal(3, await CountPortfoliosAsync(reread, account));
    }

    private async Task<ExchangeAccountSyncResult> SynchronizeAsync(
        ExchangeAccount account,
        TestPrivateProvider provider,
        TimeProvider clock)
    {
        await using var context = fixture.CreateContext();
        return await new ExchangeAccountSyncService(
                new ExchangeAccountRepository(context),
                new TestCredentialStore(),
                new TestPrivateProviderFactory(provider),
                new PositionRepository(context),
                new PortfolioStateRepository(context),
                new ExchangeAccountSyncTransaction(context),
                new ApplicationEventOutbox(context),
                clock)
            .SynchronizeAsync(account.UserId, account.Id);
    }

    private async Task SeedPositionsAsync(ExchangeAccount account, params Position[] positions)
    {
        await using var context = fixture.CreateContext();
        var repository = new PositionRepository(context);
        foreach (var position in positions)
        {
            await repository.SaveAsync(account.UserId, position, expectedVersion: null);
        }
    }

    private async Task<PortfolioState> LatestPortfolioAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var latest = await new PortfolioStateRepository(context).GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(latest);
        return latest;
    }

    private async Task<RealtimeObservationState> ReadStateAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var state = await new RealtimeObservationStateStore(context).GetAsync(account.UserId, account.Id);
        Assert.NotNull(state);
        return state.Value;
    }

    private async Task<(int Portfolios, int Outbox)> CountWritesAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        return await CountWritesAsync(context, account);
    }

    private static async Task<(int Portfolios, int Outbox)> CountWritesAsync(
        TradeSystemDbContext context,
        ExchangeAccount account) =>
        (await CountPortfoliosAsync(context, account), await CountOutboxAsync(context, account));

    private static async Task<Position> SinglePositionAsync(TradeSystemDbContext context, ExchangeAccount account) =>
        Assert.Single(await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id)).Value;

    private static void AssertHistory(Position position, params (PositionChangeKind Kind, DateTimeOffset OccurredAt)[] expected) =>
        Assert.Equal(expected, position.Changes.Select(change => (change.Kind, change.OccurredAt)).ToArray());
}
