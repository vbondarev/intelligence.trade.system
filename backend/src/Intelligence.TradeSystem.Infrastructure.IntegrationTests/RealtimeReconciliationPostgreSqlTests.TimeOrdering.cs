using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
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
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        await using var services = CreateApplicationServices(account, provider, clock);
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var coordinator = new ExchangeAccountObservationCoordinator(
            scopeFactory,
            clock,
            bufferCapacity: 1);
        Assert.True((await coordinator.SynchronizeAsync(account.UserId, account.Id)).FullReconciliationCommitted);
        var reconciled = await ReadStateAsync(account);
        Assert.False(reconciled.RecoveryRequired);
        Assert.Equal(T1.AddMinutes(1), reconciled.LastFullyReconciledAt);
        var openAt = T1.AddMinutes(2);
        provider.SetPosition(OpenPosition());
        provider.ObservedAt = openAt.AddSeconds(1);
        Assert.True(coordinator.TryEnqueue(account.UserId, generation, OpenObservation(account, openAt, 10)));
        clock.UtcNow = openAt.AddSeconds(1);
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
            AssertHistory(position, (PositionChangeKind.New, openAt.AddSeconds(1)));
            Assert.Equal(10, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
            Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
        }

        var restarted = new ExchangeAccountObservationCoordinator(
            scopeFactory,
            new FixedTimeProvider(T1.AddMinutes(3)),
            bufferCapacity: 1);
        Assert.Empty(await restarted.ProcessPendingAsync(account.UserId, account.Id));
        Assert.True((await ReadStateAsync(account)).RecoveryRequired);
        clock.UtcNow = T1.AddMinutes(3);
        provider.ObservedAt = T1.AddMinutes(3);
        provider.ClearPositions();
        provider.SetPosition(OpenPosition(size: 2m));

        var recovery = await restarted.SynchronizeAsync(account.UserId, account.Id);

        Assert.True(
            recovery.FullReconciliationCommitted,
            $"Outcome={recovery.Outcome}; positions accepted={recovery.AcceptedPositionsObservation}; " +
            $"positions complete={recovery.FullPositionsCompleteAccepted}; " +
            $"balance read={recovery.BalanceReadStartedAt is not null}");
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

    /// <summary>S-10: более новая ревизия при откате ReceivedAt сохраняет last-known и требует recovery.</summary>
    [Fact]
    public async Task Newer_revision_with_rolled_back_received_at_requires_recovery_without_writes()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1.AddSeconds(10), 1))).Outcome);
        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);

        var rolledBack = OpenObservation(account, T1.AddSeconds(11), 2, OpenPosition(size: 2m)) with
        {
            ReceivedAt = T1.AddSeconds(5),
        };
        var result = await ApplyAsync(account, generation, rolledBack);

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousObservationOrder, result.ReasonCode);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(1m, position.Size);
        Assert.Equal(T1.AddSeconds(10), position.LastObservedAt);
        Assert.Equal(1, await CountChangesAsync(verification, position.Id));
        Assert.Equal(1, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
        Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
    }

    /// <summary>S-19: wallet с более новой биржевой отметкой и откатившимся ReceivedAt не меняет капитал.</summary>
    [Fact]
    public async Task Newer_wallet_with_rolled_back_received_at_requires_recovery_without_capital_writes()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await using (var context = fixture.CreateContext())
        {
            Assert.Equal(
                RealtimeObservationApplyOutcome.Applied,
                (await CreateWalletService(context).ApplyAsync(
                    account.UserId,
                    generation,
                    WalletObservation(account, T1.AddSeconds(10), totalEquity: 1_000m))).Outcome);
        }

        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);
        RealtimeObservationApplyResult result;
        await using (var context = fixture.CreateContext())
        {
            result = await CreateWalletService(context).ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(11), totalEquity: 1_100m) with
                {
                    ReceivedAt = T1.AddSeconds(5),
                });
        }

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousObservationOrder, result.ReasonCode);
        await using var verification = fixture.CreateContext();
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(1_000m, portfolio!.Capital.TotalEquity);
        Assert.Equal(T1.AddSeconds(10), portfolio.Capital.ObservedAt);
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(10), stored!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
    }

    /// <summary>
    /// S-23 / B-01: Failed balance + Complete positions фиксирует size=3; буфер size=2 не откатывает
    /// позицию, историю, cursor, portfolio и outbox.
    /// </summary>
    [Fact]
    public async Task Failed_balance_complete_positions_keeps_rest_size_against_buffered_regression()
    {
        var (account, generation) = await CreateObservedAccountAsync(recoveryRequired: true);
        var clock = new MutableTimeProvider(T1);
        ExchangeAccountObservationCoordinator? coordinator = null;
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            BalanceFails = true,
        };
        provider.SetPosition(OpenPosition(size: 3m));
        provider.BeforePositionsResponse = () =>
        {
            provider.BeforePositionsResponse = null;
            clock.UtcNow = T1.AddSeconds(2);
            Assert.True(coordinator!.TryEnqueue(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(2), 1, OpenPosition(size: 2m))));
            clock.UtcNow = T1.AddSeconds(4);
            return Task.CompletedTask;
        };
        await using var services = CreateApplicationServices(account, provider, clock);
        coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var sync = await coordinator.SynchronizeAsync(account.UserId, account.Id);
        var (portfoliosAfterSync, outboxAfterSync) = await CountWritesAsync(account);
        clock.UtcNow = T1.AddSeconds(5);
        var pending = await coordinator.ProcessPendingAsync(account.UserId, account.Id);

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, sync.Outcome);
        Assert.False(sync.FullReconciliationCommitted);
        Assert.True(sync.AcceptedPositionsObservation);
        Assert.Null(sync.BalanceReadStartedAt);
        Assert.True(Assert.Single(pending).ConfirmedByRest);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(3m, position.Size);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        // ObservedAt REST — начало чтения (T1); завершение T1+4 покрывает буфер size=2.
        AssertHistory(position, (PositionChangeKind.New, T1));
        var cursor = await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(1, cursor!.Value.LastProviderSequence);
        Assert.True(await CountPortfoliosAsync(verification, account) >= portfoliosAfterSync);
        Assert.True(await CountOutboxAsync(verification, account) >= outboxAfterSync);
        var state = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
        Assert.True(state!.Value.RecoveryRequired);
    }

    /// <summary>
    /// S-20 / B-02: успешный balance + Failed positions сохраняет окно баланса; wallet внутри
    /// интервала требует recovery без отката капитала.
    /// </summary>
    [Fact]
    public async Task Complete_balance_failed_positions_preserves_balance_window_against_wallet_regression()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1);
        ExchangeAccountObservationCoordinator? coordinator = null;
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            Status = OpenPositionsObservationStatus.Failed,
            Balance = new AccountBalance(AccountType.Unified, 200m, 190m, 150m, 10m, []),
        };
        provider.BeforeBalanceResponse = () =>
        {
            provider.BeforeBalanceResponse = null;
            clock.UtcNow = T1.AddSeconds(1);
            Assert.True(coordinator!.TryEnqueue(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(1), totalEquity: 100m)));
            clock.UtcNow = T1.AddSeconds(2);
            return Task.CompletedTask;
        };
        await using var services = CreateApplicationServices(account, provider, clock);
        coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var sync = await coordinator.SynchronizeAsync(account.UserId, account.Id);
        var (portfoliosAfterSync, outboxAfterSync) = await CountWritesAsync(account);
        clock.UtcNow = T1.AddSeconds(3);
        var pending = await coordinator.ProcessPendingAsync(account.UserId, account.Id);

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, sync.Outcome);
        Assert.False(sync.FullReconciliationCommitted);
        Assert.Equal(T1, sync.BalanceReadStartedAt);
        Assert.Equal(T1.AddSeconds(2), sync.BalanceReadCompletedAt);
        Assert.Contains(
            pending,
            result => result.Outcome == RealtimeObservationApplyOutcome.RecoveryRequired &&
                      result.ReasonCode == RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        await using var verification = fixture.CreateContext();
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(200m, portfolio!.Capital.TotalEquity);
        Assert.True(portfolio.Capital.ObservedAt >= T1.AddSeconds(2));
        Assert.False(portfolio.PositionsFullyReconciled);
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.True(stored!.Value.LastAppliedBalanceObservationAt >= T1.AddSeconds(2));
        Assert.Null(stored.Value.LastSyncedAt);
        // ProcessPending после ambiguous wallet запускает recovery-сверку: счётчики записей могут вырасти.
        Assert.True(await CountPortfoliosAsync(verification, account) >= portfoliosAfterSync);
        Assert.True(await CountOutboxAsync(verification, account) >= outboxAfterSync);
        var state = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
        Assert.True(state!.Value.RecoveryRequired);
    }

    /// <summary>S-20: wallet во время GetWalletBalanceAsync не откатывает капитал, принятый REST.</summary>
    [Fact]
    public async Task Wallet_during_balance_read_requires_recovery_and_keeps_rest_capital()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1);
        ExchangeAccountObservationCoordinator? coordinator = null;
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            Balance = new AccountBalance(AccountType.Unified, 200m, 190m, 150m, 10m, []),
        };
        provider.BeforeBalanceResponse = () =>
        {
            provider.BeforeBalanceResponse = null;
            clock.UtcNow = T1.AddSeconds(1);
            Assert.True(coordinator!.TryEnqueue(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(1), totalEquity: 100m)));
            clock.UtcNow = T1.AddSeconds(2);
            return Task.CompletedTask;
        };
        await using var services = CreateApplicationServices(account, provider, clock);
        coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var sync = await coordinator.SynchronizeAsync(account.UserId, account.Id);
        clock.UtcNow = T1.AddSeconds(3);
        var pending = await coordinator.ProcessPendingAsync(account.UserId, account.Id);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, sync.Outcome);
        Assert.True(sync.FullReconciliationCommitted);
        var ambiguous = Assert.Single(pending);
        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, ambiguous.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousWithRestBalance, ambiguous.ReasonCode);
        await using var verification = fixture.CreateContext();
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(200m, portfolio!.Capital.TotalEquity);
        Assert.True(portfolio.Capital.ObservedAt >= T1.AddSeconds(2));
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.True(stored!.Value.LastAppliedBalanceObservationAt >= T1.AddSeconds(2));
        var state = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
        Assert.False(state!.Value.RecoveryRequired);
    }

    /// <summary>
    /// R-208-03: Synchronized при Superseded Failed positions не создаёт FullSyncWindow; buffered
    /// stream-revision применяется напрямую и сохраняется в PostgreSQL.
    /// </summary>
    [Fact]
    public async Task Synchronized_balance_with_superseded_failed_positions_applies_stream_without_scoped_rest()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var bootstrapProvider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(10) };
        bootstrapProvider.SetPosition(OpenPosition(size: 1m));
        var bootstrap = await SynchronizeAsync(
            account,
            bootstrapProvider,
            new FixedTimeProvider(T1.AddSeconds(10)));
        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, bootstrap.Outcome);
        Assert.True(bootstrap.AcceptedPositionsObservation);
        Assert.True(bootstrap.FullReconciliationCommitted);

        await using (var watermarkContext = fixture.CreateContext())
        {
            var stored = await new ExchangeAccountRepository(watermarkContext)
                .GetByIdAsync(account.UserId, account.Id);
            Assert.NotNull(stored);
            stored.Value.AdvanceObservationWatermark(
                ExchangeAccountObservationResource.Positions,
                T1.AddSeconds(15));
            await new ExchangeAccountRepository(watermarkContext)
                .SaveAsync(account.UserId, stored.Value, stored.Version);
        }

        var clock = new MutableTimeProvider(T1.AddSeconds(20));
        ExchangeAccountObservationCoordinator? coordinator = null;
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            Status = OpenPositionsObservationStatus.Failed,
            PositionsObservedAt = T1.AddSeconds(14),
            Balance = new AccountBalance(AccountType.Unified, 2_100m, 2_000m, 1_600m, 100m, []),
        };
        provider.SetPosition(OpenPosition(size: 1m));
        provider.BeforePositionsResponse = () =>
        {
            provider.BeforePositionsResponse = null;
            clock.UtcNow = T1.AddSeconds(21);
            Assert.True(coordinator!.TryEnqueue(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(21), 2, OpenPosition(size: 2m))));
            clock.UtcNow = T1.AddSeconds(22);
            return Task.CompletedTask;
        };
        await using var services = CreateApplicationServices(account, provider, clock);
        coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var sync = await coordinator.SynchronizeAsync(account.UserId, account.Id);
        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, sync.Outcome);
        Assert.False(sync.AcceptedPositionsObservation);
        Assert.False(sync.FullReconciliationCommitted);

        provider.Status = OpenPositionsObservationStatus.Complete;
        provider.PositionsObservedAt = null;
        var requestsBeforePending = provider.PositionRequests.Count;
        clock.UtcNow = T1.AddSeconds(23);
        var pending = await coordinator.ProcessPendingAsync(account.UserId, account.Id);

        var applied = Assert.Single(pending);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, applied.Outcome);
        Assert.False(applied.ConfirmedByRest);
        Assert.Equal(requestsBeforePending, provider.PositionRequests.Count);
        Assert.All(provider.PositionRequests, symbol => Assert.Null(symbol));

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(2m, position.Size);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(T1.AddSeconds(21), position.LastObservedAt);
        AssertHistory(
            position,
            (PositionChangeKind.New, T1.AddSeconds(10)),
            (PositionChangeKind.Increased, T1.AddSeconds(21)));
        Assert.Equal(
            2,
            (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!
                .Value.LastProviderSequence);
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(2m, Assert.Single(portfolio!.Positions).Size);
        Assert.True(portfolio.PositionsFullyReconciled);
        Assert.Equal(2_100m, portfolio.Capital.TotalEquity);
        Assert.True(await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged) >= 1);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
        var state = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
        Assert.False(state!.Value.RecoveryRequired);
        Assert.Equal(T1, state.Value.LastFullyReconciledAt);
    }

    /// <summary>
    /// R-208-01: Failed REST не создаёт ложное окно; более новая stream-revision восстанавливает позицию.
    /// </summary>
    [Fact]
    public async Task Failed_rest_then_newer_stream_observation_persists_recovered_position()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);

        var failed = await SynchronizeAsync(
            account,
            new TestPrivateProvider { ObservedAt = T1.AddSeconds(10), Status = OpenPositionsObservationStatus.Failed },
            new FixedTimeProvider(T1.AddSeconds(10)));
        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, failed.Outcome);
        Assert.False(failed.AcceptedPositionsObservation);
        Assert.False(failed.FullReconciliationCommitted);

        var recovered = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(12), 2, OpenPosition(size: 2m)));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, recovered.Outcome);
        Assert.False(recovered.ConfirmedByRest);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(2m, position.Size);
        Assert.Equal(T1.AddSeconds(12), position.LastObservedAt);
        AssertHistory(
            position,
            (PositionChangeKind.New, T1),
            (PositionChangeKind.MarkedUnknown, T1.AddSeconds(10)),
            (PositionChangeKind.Increased, T1.AddSeconds(12)));
        Assert.Equal(2, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(2m, Assert.Single(portfolio!.Positions).Size);
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(2, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        var state = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
        Assert.False(state!.Value.RecoveryRequired);
        Assert.Equal(T1, state.Value.LastFullyReconciledAt);
    }

    /// <summary>
    /// R-208-02: противоречащий Complete с равным временем не меняет подтверждённую позицию.
    /// </summary>
    [Fact]
    public async Task Equal_timestamp_conflicting_complete_keeps_confirmed_position()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        int changedBefore;
        int closedBefore;
        await using (var beforeContext = fixture.CreateContext())
        {
            changedBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PositionChanged);
            closedBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PositionClosed);
        }

        var provider = new TestPrivateProvider { ObservedAt = T1 };
        provider.SetPosition(OpenPosition(size: 2m));
        var sync = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, sync.Outcome);
        Assert.False(sync.FullReconciliationCommitted);
        Assert.True(sync.AcceptedPositionsObservation);
        await using (var verification = fixture.CreateContext())
        {
            var position = await SinglePositionAsync(verification, account);
            Assert.Equal(PositionTrackingState.Active, position.TrackingState);
            Assert.Equal(1m, position.Size);
            Assert.Equal(T1, position.LastObservedAt);
            Assert.Single(position.Changes);
            var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
            Assert.False(portfolio!.PositionsFullyReconciled);
            Assert.Equal(1m, Assert.Single(portfolio.Positions).Size);
            Assert.Equal(changedBefore, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
            Assert.Equal(closedBefore, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
            var state = await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id);
            Assert.False(state!.Value.RecoveryRequired);
            Assert.Equal(T1, state.Value.LastFullyReconciledAt);
            Assert.Equal(1, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
        }

        var recovery = new TestPrivateProvider { ObservedAt = T1.AddSeconds(30) };
        recovery.SetPosition(OpenPosition(size: 2m));
        await using var services = CreateApplicationServices(
            account,
            recovery,
            new FixedTimeProvider(T1.AddSeconds(30)));
        var recovered = await services.GetRequiredService<ExchangeAccountObservationCoordinator>()
            .SynchronizeAsync(account.UserId, account.Id);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, recovered.Outcome);
        Assert.True(recovered.FullReconciliationCommitted);
        await using var reread = fixture.CreateContext();
        var recoveredPosition = await SinglePositionAsync(reread, account);
        Assert.Equal(2m, recoveredPosition.Size);
        Assert.Equal(T1.AddSeconds(30), recoveredPosition.LastObservedAt);
        var recoveredState = await new RealtimeObservationStateStore(reread).GetAsync(account.UserId, account.Id);
        Assert.False(recoveredState!.Value.RecoveryRequired);
        Assert.Equal(T1.AddSeconds(30), recoveredState.Value.LastFullyReconciledAt);
    }

    /// <summary>
    /// R-208-02: scoped confirmation с равным временем и пустым Complete не закрывает позицию.
    /// </summary>
    [Fact]
    public async Task Equal_timestamp_empty_scoped_rest_does_not_close_or_advance_cursor()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        var confirmation = new TestPrivateProvider { ObservedAt = T1 };
        var (portfoliosBefore, outboxBefore) = await CountWritesAsync(account);
        var close = CloseObservation(account, T1.AddSeconds(2), 2) with { ReceivedAt = T1 };

        var result = await ApplyAsync(
            account,
            generation,
            close,
            confirmation,
            new FixedTimeProvider(T1.AddSeconds(3)),
            confirmWithRest: true);

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RestConfirmationIncomplete, result.ReasonCode);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(1m, position.Size);
        Assert.Equal(T1, position.LastObservedAt);
        Assert.Equal(1, (await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)))!.Value.LastProviderSequence);
        Assert.Equal((portfoliosBefore, outboxBefore), await CountWritesAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
    }

    /// <summary>R-208-06 / G6: equal-timestamp Short не сохраняется поверх Active Long.</summary>
    [Fact]
    public async Task Equal_timestamp_opposite_side_does_not_persist_conflicting_lifecycle()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        var provider = new TestPrivateProvider { ObservedAt = T1 };
        provider.SetPosition(OpenPosition(side: PositionSide.Short, size: 2m));

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(10)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        var active = Assert.Single(positions, versioned => versioned.Value.TrackingState != PositionTrackingState.Closed);
        Assert.Equal(PositionSide.Long, active.Value.ExchangePositionKey.PositionSide);
        Assert.Equal(1m, active.Value.Size);
        Assert.Equal(T1, active.Value.LastObservedAt);
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.False((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>R-208-05 / G4: full REST Short старше закрытия Long не создаёт lifecycle.</summary>
    [Fact]
    public async Task Full_rest_opposite_side_older_than_closure_does_not_create_position()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        var closeProvider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(10) };
        var closeSync = await SynchronizeAsync(account, closeProvider, new FixedTimeProvider(T1.AddSeconds(10)));
        Assert.True(closeSync.FullReconciliationCommitted);
        await using (var afterClose = fixture.CreateContext())
        {
            var closedPosition = await SinglePositionAsync(afterClose, account);
            Assert.Equal(PositionTrackingState.Closed, closedPosition.TrackingState);
            Assert.Equal(T1.AddSeconds(10), closedPosition.ClosedAt);
        }

        var staleShort = new TestPrivateProvider { ObservedAt = T1.AddSeconds(6) };
        staleShort.SetPosition(OpenPosition(side: PositionSide.Short, size: 2m));
        var result = await SynchronizeAsync(account, staleShort, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        var onlyClosed = Assert.Single(positions);
        Assert.Equal(PositionTrackingState.Closed, onlyClosed.Value.TrackingState);
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
    }

    /// <summary>R-208-07 / G7: Complete Long+Short idx=0 не подтверждает полноту и не сохраняет обе стороны.</summary>
    [Fact]
    public async Task Complete_conflicting_one_way_sides_do_not_persist_both_lifecycles()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        provider.SetPosition(OpenPosition(side: PositionSide.Long, size: 1m, positionIdx: 0));
        provider.SetPosition(OpenPosition(side: PositionSide.Short, size: 2m, positionIdx: 0));

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        Assert.Empty(positions);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.False((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>R-208-07 / G8: hedge Long idx=1 и Short idx=2 принимаются.</summary>
    [Fact]
    public async Task Complete_hedge_sides_on_different_idx_are_persisted()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        provider.SetPosition(OpenPosition(side: PositionSide.Long, size: 1m, positionIdx: 1));
        provider.SetPosition(OpenPosition(side: PositionSide.Short, size: 2m, positionIdx: 2));

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.True(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        Assert.Equal(2, positions.Count);
        Assert.All(positions, versioned => Assert.Equal(PositionTrackingState.Active, versioned.Value.TrackingState));
        Assert.True((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>
    /// R-208-08: конфликт сторон ETH блокирует смену стороны BTC — второй незакрытый lifecycle не сохраняется.
    /// </summary>
    [Fact]
    public async Task Side_change_blocked_by_eth_conflict_keeps_single_btc_lifecycle()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        provider.SetPosition(OpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m));
        provider.SetPosition(OpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m));
        provider.SetPosition(OpenPosition(symbol: "ETHUSDT", side: PositionSide.Short, size: 4m));

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        var btcOpen = positions
            .Select(versioned => versioned.Value)
            .Where(position =>
                position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT" &&
                position.TrackingState != PositionTrackingState.Closed)
            .ToArray();
        Assert.Single(btcOpen);
        Assert.Equal(PositionSide.Long, btcOpen[0].ExchangePositionKey.PositionSide);
        Assert.Null(btcOpen[0].ClosedAt);
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
        Assert.False((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>
    /// R-208-09: противоречащие REST-дубликаты BTC не меняют persisted размер; порядок строк не влияет.
    /// </summary>
    [Theory]
    [InlineData(2.0, 3.0)]
    [InlineData(3.0, 2.0)]
    public async Task Conflicting_duplicate_rest_rows_do_not_change_persisted_size(double firstSize, double secondSize)
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        provider.SetPosition(OpenPosition(size: (decimal)firstSize));
        provider.SetPosition(OpenPosition(size: (decimal)secondSize));

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(1m, position.Size);
        Assert.Equal(T1, position.LastObservedAt);
        AssertHistory(position, (PositionChangeKind.New, T1));
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        Assert.False((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>
    /// R-208-08 / A3: успешная смена стороны без конфликта закрывает Long и создаёт Short атомарно.
    /// </summary>
    [Fact]
    public async Task Clean_complete_side_change_closes_long_and_opens_short()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        await using (var before = fixture.CreateContext())
        {
            var longPosition = await SinglePositionAsync(before, account);
            Assert.Equal(PositionSide.Long, longPosition.ExchangePositionKey.PositionSide);
        }

        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        provider.SetPosition(OpenPosition(side: PositionSide.Short, size: 2m));
        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.True(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        Assert.Equal(2, positions.Count);
        var closedLong = Assert.Single(
            positions,
            versioned => versioned.Value.ExchangePositionKey.PositionSide == PositionSide.Long);
        Assert.Equal(PositionTrackingState.Closed, closedLong.Value.TrackingState);
        Assert.Equal(T1.AddSeconds(20), closedLong.Value.ClosedAt);
        var activeShort = Assert.Single(
            positions,
            versioned => versioned.Value.TrackingState != PositionTrackingState.Closed);
        Assert.Equal(PositionSide.Short, activeShort.Value.ExchangePositionKey.PositionSide);
        Assert.Equal(2m, activeShort.Value.Size);
        Assert.Equal(2, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
        Assert.True((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>
    /// R-208-10: обе перестановки REST-строк не сохраняют BTC Short при ClosedAt ETH=T30.
    /// ETH закрыт через Seed без продвижения account positions watermark до T30.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Side_change_blocked_by_closed_eth_is_order_independent(bool btcShortFirst)
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        var eth = Position.Create(
            ExchangePositionKey.Create(account.Id, InstrumentId.From("ETHUSDT"), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            T1,
            T1,
            averageEntryPrice: 100m,
            leverage: 2m);
        eth.Close(T1.AddSeconds(30), PositionChangeCause.MissingFromCompleteObservation);
        await SeedPositionsAsync(account, eth);

        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        if (btcShortFirst)
        {
            provider.SetPosition(OpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m));
            provider.SetPosition(OpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m));
        }
        else
        {
            provider.SetPosition(OpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m));
            provider.SetPosition(OpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m));
        }

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        var btcOpen = positions
            .Select(versioned => versioned.Value)
            .Where(position =>
                position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT" &&
                position.TrackingState != PositionTrackingState.Closed)
            .ToArray();
        Assert.Single(btcOpen);
        Assert.Equal(PositionSide.Long, btcOpen[0].ExchangePositionKey.PositionSide);
        Assert.Null(btcOpen[0].ClosedAt);
        Assert.DoesNotContain(
            positions,
            versioned =>
                versioned.Value.ExchangePositionKey.InstrumentId.Value == "BTCUSDT" &&
                versioned.Value.ExchangePositionKey.PositionSide == PositionSide.Short);
        var ethPersisted = Assert.Single(
            positions,
            versioned => versioned.Value.ExchangePositionKey.InstrumentId.Value == "ETHUSDT");
        Assert.Equal(PositionTrackingState.Closed, ethPersisted.Value.TrackingState);
        Assert.Equal(T1.AddSeconds(30), ethPersisted.Value.ClosedAt);
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
        Assert.False((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>
    /// R-208-11: дубликаты с разным SettlementAsset не создают lifecycle; порядок не влияет.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conflicting_settlement_asset_duplicates_do_not_create_lifecycle(bool usdtFirst)
    {
        var (account, _) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        if (usdtFirst)
        {
            provider.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdt));
            provider.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdc));
        }
        else
        {
            provider.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdc));
            provider.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdt));
        }

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        Assert.Empty(positions);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.False((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
    }

    /// <summary>
    /// R-208-12: Long без SettlementAsset + Short USDT одного idx=0 не создаёт lifecycle; порядок не влияет.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejected_missing_settlement_side_conflict_does_not_create_lifecycle(bool missingSettlementFirst)
    {
        var (account, _) = await CreateObservedAccountAsync();
        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        var missing = OpenPosition(side: PositionSide.Long, size: 1m) with { SettlementAsset = null };
        var shortRow = OpenPosition(side: PositionSide.Short, size: 2m, settlementAsset: Usdt);
        if (missingSettlementFirst)
        {
            provider.SetPosition(missing);
            provider.SetPosition(shortRow);
        }
        else
        {
            provider.SetPosition(shortRow);
            provider.SetPosition(missing);
        }

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id);
        Assert.Empty(positions);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
    }

    /// <summary>
    /// R-208-13: отклонённый USDC-дубликат не меняет persisted Size; порядок строк не влияет.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejected_settlement_duplicate_does_not_update_tracked_position(bool usdtFirst)
    {
        var (account, _) = await CreateObservedAccountAsync();
        var tracked = Position.Create(
            ExchangePositionKey.Create(account.Id, InstrumentId.From("BTCUSDT"), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            T1.AddSeconds(10),
            T1.AddSeconds(10),
            averageEntryPrice: 100m,
            leverage: 2m);
        await SeedPositionsAsync(account, tracked);

        var provider = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        var usdtRow = OpenPosition(size: 2m, settlementAsset: Usdt);
        var usdcRow = OpenPosition(size: 3m, settlementAsset: Usdc);
        if (usdtFirst)
        {
            provider.SetPosition(usdtRow);
            provider.SetPosition(usdcRow);
        }
        else
        {
            provider.SetPosition(usdcRow);
            provider.SetPosition(usdtRow);
        }

        var result = await SynchronizeAsync(account, provider, new FixedTimeProvider(T1.AddSeconds(20)));

        Assert.False(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(1m, position.Size);
        Assert.Equal(Usdt, position.SettlementAsset);
        Assert.Equal(T1.AddSeconds(10), position.LastObservedAt);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
    }

    /// <summary>
    /// R-208-14 / B1: wallet другого экземпляра во время REST-чтения не допускает откат капитала
    /// устаревшим REST-балансом; watermark баланса не продвигается ложно.
    /// </summary>
    [Fact]
    public async Task Cross_instance_wallet_during_rest_balance_read_preserves_newer_capital()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []),
        };
        provider.BeforeBalanceResponse = async () =>
        {
            provider.BeforeBalanceResponse = null;
            clock.UtcNow = T1.AddSeconds(2);
            await using var walletContext = fixture.CreateContext();
            var walletResult = await CreateWalletService(walletContext)
                .ApplyAsync(
                    account.UserId,
                    generation,
                    WalletObservation(account, T1.AddSeconds(2), totalEquity: 1_100m));
            Assert.Equal(RealtimeObservationApplyOutcome.Applied, walletResult.Outcome);
            clock.UtcNow = T1.AddSeconds(4);
        };

        var result = await SynchronizeAsync(account, provider, clock);

        Assert.False(result.FullReconciliationCommitted);
        Assert.Null(result.BalanceReadStartedAt);
        Assert.Null(result.BalanceReadCompletedAt);
        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, result.Outcome);
        await using var verification = fixture.CreateContext();
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(1_100m, portfolio!.Capital.TotalEquity);
        Assert.Equal(T1.AddSeconds(2), portfolio.Capital.ObservedAt);
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(2), stored!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal("balance_ambiguous", stored.Value.LastError);
        Assert.Null(stored.Value.LastSyncedAt);
    }

    /// <summary>
    /// H-04: неоднозначный REST-баланс сохраняется как <c>balance_ambiguous</c>, даже когда
    /// REST-позиции тоже не принимаются (watermark позиций новее наблюдения).
    /// </summary>
    [Fact]
    public async Task Ambiguous_rest_balance_with_superseded_positions_persists_balance_ambiguous()
    {
        var t1 = T1;
        var t2 = T1.AddSeconds(2);
        var t4 = T1.AddSeconds(4);
        var t5 = T1.AddSeconds(5);
        var (account, generation) = await CreateObservedAccountAsync();
        await using (var walletContext = fixture.CreateContext())
        {
            var walletResult = await CreateWalletService(walletContext)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, t2, totalEquity: 1_100m));
            Assert.Equal(RealtimeObservationApplyOutcome.Applied, walletResult.Outcome);
        }

        var setup = new TestPrivateProvider
        {
            ObservedAt = t2,
            PositionsObservedAt = t5,
            Balance = new AccountBalance(AccountType.Unified, 1_100m, 1_000m, 900m, 20m, []),
        };
        var setupResult = await SynchronizeAsync(account, setup, new FixedTimeProvider(t5));
        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, setupResult.Outcome);
        Assert.False(setupResult.FullReconciliationCommitted);

        var degradedBefore = 0;
        var openedBefore = 0;
        var changedBefore = 0;
        var closedBefore = 0;
        var portfolioUpdatedBefore = 0;
        var balanceEvidenceBefore = 0;
        await using (var beforeContext = fixture.CreateContext())
        {
            var beforeAccount = await new ExchangeAccountRepository(beforeContext)
                .GetByIdAsync(account.UserId, account.Id);
            Assert.Equal(t2, beforeAccount!.Value.LastAppliedBalanceObservationAt);
            Assert.Equal(t5, beforeAccount.Value.LastAppliedPositionsObservationAt);
            degradedBefore = await CountOutboxAsync(
                beforeContext,
                account,
                ApplicationEventTypes.ExchangeAccountSyncDegraded);
            openedBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PositionOpened);
            changedBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PositionChanged);
            closedBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PositionClosed);
            portfolioUpdatedBefore = await CountOutboxAsync(
                beforeContext,
                account,
                ApplicationEventTypes.PortfolioUpdated);
            balanceEvidenceBefore = await beforeContext.ExchangeRestOrderingIntervals
                .AsNoTracking()
                .CountAsync(interval =>
                    interval.ExchangeAccountId == account.Id.Value &&
                    interval.Kind == ExchangeRestOrderingKind.Balance);
        }

        var clock = new MutableTimeProvider(t1);
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = t4,
            Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []),
        };
        provider.BeforeBalanceResponse = () =>
        {
            clock.UtcNow = t4;
            return Task.CompletedTask;
        };

        var result = await SynchronizeAsync(account, provider, clock);

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, result.Outcome);
        Assert.False(result.FullReconciliationCommitted);
        Assert.Null(result.BalanceReadStartedAt);
        Assert.Null(result.BalanceReadCompletedAt);
        Assert.False(result.AcceptedPositionsObservation);
        await using var verification = fixture.CreateContext();
        var portfolio = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(1_100m, portfolio!.Capital.TotalEquity);
        Assert.Equal(t2, portfolio.Capital.ObservedAt);
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal("balance_ambiguous", stored!.Value.LastError);
        Assert.Equal(t2, stored.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(t5, stored.Value.LastAppliedPositionsObservationAt);
        Assert.Equal(
            degradedBefore + 1,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.ExchangeAccountSyncDegraded));
        Assert.Equal(
            portfolioUpdatedBefore + 1,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(
            balanceEvidenceBefore,
            await verification.ExchangeRestOrderingIntervals
                .AsNoTracking()
                .CountAsync(interval =>
                    interval.ExchangeAccountId == account.Id.Value &&
                    interval.Kind == ExchangeRestOrderingKind.Balance));
        Assert.Equal(
            openedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(
            changedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        Assert.Equal(
            closedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
    }

    /// <summary>
    /// R-208-11 / R11-H: после конфликта по SettlementAsset чистый Complete завершает recovery.
    /// </summary>
    [Fact]
    public async Task Clean_complete_after_settlement_conflict_commits_full_reconciliation()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var conflict = new TestPrivateProvider { ObservedAt = T1.AddSeconds(20) };
        conflict.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdt));
        conflict.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdc));
        Assert.False(
            (await SynchronizeAsync(account, conflict, new FixedTimeProvider(T1.AddSeconds(20))))
                .FullReconciliationCommitted);

        var recovery = new TestPrivateProvider { ObservedAt = T1.AddSeconds(40) };
        recovery.SetPosition(OpenPosition(size: 2m, settlementAsset: Usdt));
        var result = await SynchronizeAsync(account, recovery, new FixedTimeProvider(T1.AddSeconds(40)));

        Assert.True(result.FullReconciliationCommitted);
        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(Usdt, position.SettlementAsset);
        Assert.Equal(2m, position.Size);
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.True((await LatestPortfolioAsync(account)).PositionsFullyReconciled);
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
                new ExchangeRestOrderingEvidenceStore(context),
                new WalletObservationCursorStore(context),
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
