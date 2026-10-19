using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

/// <summary>
/// Package 1b: persisted REST ordering evidence для межэкземплярного согласования REST/WebSocket.
/// </summary>
public sealed class RestOrderingEvidenceTests
{
    [Fact]
    public async Task Rest_positions_commit_blocks_delayed_ws_size_rollback_without_position_changed()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullPositionsCompleteAccepted.Should().BeTrue();
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete);

        var delayed = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition(size: 1m)));

        delayed.Outcome.Should().BeOneOf(
            RealtimeObservationApplyOutcome.RecoveryRequired,
            RealtimeObservationApplyOutcome.Applied);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            delayed.ConfirmedByRest.Should().BeTrue();
        }

        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Wallet_commit_then_older_rest_balance_does_not_overwrite_capital()
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(30);
        var wallet = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.WalletObservation(T0.AddSeconds(30), totalEquity: 3_000m));
        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(3_000m);

        world.Provider.Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []);
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(25);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(35);

        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        sync.BalanceReadStartedAt.Should().BeNull();
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(3_000m);
        world.Account.LastError.Should().Be("balance_ambiguous");
        world.RestOrderingIntervals.Should().NotContain(interval =>
            interval.Kind == ExchangeRestOrderingKind.Balance &&
            interval.StartedAt == T0.AddSeconds(25));
    }

    [Fact]
    public async Task Rest_balance_commit_then_wallet_inside_interval_requires_recovery_not_silent_stale()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync.BalanceReadStartedAt.Should().Be(T0.AddSeconds(10));
        sync.BalanceReadCompletedAt.Should().Be(T0.AddSeconds(20));
        var acceptedEquity = world.LatestPortfolio!.Capital.TotalEquity;

        var wallet = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.WalletObservation(T0.AddSeconds(15), totalEquity: 9_999m));

        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        wallet.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(acceptedEquity);
    }

    [Fact]
    public async Task Partial_rest_does_not_create_full_positions_complete()
    {
        var world = new RealtimeTestWorld();
        world.Provider.Status = OpenPositionsObservationStatus.Partial;
        world.Provider.SetExchangePosition(OpenPosition("BTCUSDT", size: 1m));
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(5);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.FullPositionsCompleteAccepted.Should().BeFalse();
        result.AcceptedPositionsObservation.Should().BeTrue();
        world.RestOrderingIntervals.Should().NotContain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete);
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
            interval.Symbol == "BTCUSDT");
    }

    /// <summary>
    /// P2-1: Failed balance не скрывает принятие Complete positions — evidence и результат совпадают.
    /// </summary>
    [Fact]
    public async Task Failed_balance_with_complete_positions_accepts_full_positions_without_full_reconciliation()
    {
        var world = new RealtimeTestWorld();
        world.Provider.BalanceFails = true;
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        result.PortfolioState.Should().BeNull();
        result.AcceptedPositionsObservation.Should().BeTrue();
        result.FullPositionsCompleteAccepted.Should().BeTrue();
        result.FullReconciliationCommitted.Should().BeFalse();
        result.BalanceReadStartedAt.Should().BeNull();
        result.BalanceReadCompletedAt.Should().BeNull();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.RestOrderingIntervals.Should().ContainSingle(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete);
        world.RestOrderingIntervals.Should().NotContain(interval =>
            interval.Kind == ExchangeRestOrderingKind.Balance);
    }

    [Fact]
    public async Task Failed_positions_does_not_accept_full_positions_complete()
    {
        var world = new RealtimeTestWorld();
        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(5);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        result.AcceptedPositionsObservation.Should().BeFalse();
        result.FullPositionsCompleteAccepted.Should().BeFalse();
        result.FullReconciliationCommitted.Should().BeFalse();
        world.RestOrderingIntervals.Should().NotContain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete);
    }

    [Fact]
    public async Task Scoped_symbol_evidence_does_not_affect_other_symbols()
    {
        var world = new RealtimeTestWorld();
        world.ReplaceRestOrderingIntervals(
            new ExchangeRestOrderingInterval
            {
                Id = Guid.NewGuid(),
                ExchangeAccountId = world.AccountId,
                Kind = ExchangeRestOrderingKind.ScopedSymbol,
                Symbol = "BTCUSDT",
                StartedAt = T0.AddSeconds(10),
                CompletedAt = T0.AddSeconds(20),
                RecordedAt = T0.AddSeconds(20),
            });

        world.Provider.SetExchangePosition(OpenPosition("ETHUSDT"));
        var eth = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition("ETHUSDT")));

        eth.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        eth.ConfirmedByRest.Should().BeFalse();
        world.ActivePositions.Should().Contain(position =>
            position.ExchangePositionKey.InstrumentId.Value == "ETHUSDT");
    }

    [Fact]
    public void Non_overlapping_intervals_do_not_cover_the_gap()
    {
        var accountId = ExchangeAccountId.New();
        var intervals = new[]
        {
            Interval(accountId, ExchangeRestOrderingKind.FullPositionsComplete, T0.AddSeconds(1), T0.AddSeconds(2)),
            Interval(accountId, ExchangeRestOrderingKind.FullPositionsComplete, T0.AddSeconds(4), T0.AddSeconds(5)),
        };

        RestOrderingDecision.PositionOverlapsRest(T0.AddSeconds(3), "BTCUSDT", intervals)
            .Should().BeFalse();
        RestOrderingDecision.PositionOverlapsRest(T0.AddSeconds(1) + TimeSpan.FromMilliseconds(500), "BTCUSDT", intervals)
            .Should().BeTrue();
    }

    [Fact]
    public void Missing_evidence_does_not_authorize_dangerous_direct_apply_by_default()
    {
        RestOrderingDecision.PositionOverlapsRest(T0, "BTCUSDT", []).Should().BeFalse();
        RestOrderingDecision.WalletOverlapsBalanceRead(T0, T0, []).Should().BeFalse();
    }

    [Fact]
    public async Task Older_position_evidence_does_not_prove_order_after_newer_rest_evidence_is_lost()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(1);
        world.Clock.UtcNow = T0.AddSeconds(1);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(2);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        var evidence1 = world.RestOrderingIntervals
            .Where(interval => interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
            .ToArray();
        evidence1.Should().ContainSingle();

        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync2 = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync2.FullPositionsCompleteAccepted.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.ActivePositions.Single().LastObservedAt.Should().Be(T0.AddSeconds(10));
        world.Account.LastAppliedPositionsObservationAt.Should().Be(T0.AddSeconds(10));
        world.RestOrderingIntervals.Count(interval =>
                interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
            .Should().Be(2);

        // Остаётся только evidence REST №1; evidence REST №2 удалено.
        world.ReplaceRestOrderingIntervals(evidence1);
        world.RestOrderingIntervals.Should().ContainSingle(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete &&
            interval.CompletedAt == T0.AddSeconds(2));

        // Scoped confirmation доступен и подтверждает актуальный size=3.
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Provider.SnapshotObservedAt = T0.AddMinutes(2);
        world.Clock.UtcNow = T0.AddMinutes(2);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        var changedBefore = world.Events.OfType<PositionChangedEventV1>().Count();
        var delayed = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition(size: 2m)));

        delayed.Outcome.Should().BeOneOf(
            RealtimeObservationApplyOutcome.Applied,
            RealtimeObservationApplyOutcome.RecoveryRequired);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            delayed.ConfirmedByRest.Should().BeTrue();
        }
        else
        {
            delayed.ConfirmedByRest.Should().BeFalse();
        }

        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.Events.OfType<PositionChangedEventV1>().Should().HaveCount(changedBefore);
        world.Events.OfType<PositionChangedEventV1>()
            .Should().NotContain(change => change.After.Size == 2m);
    }

    [Fact]
    public async Task Older_position_evidence_requires_recovery_when_scoped_confirmation_unavailable()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(1);
        world.Clock.UtcNow = T0.AddSeconds(1);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(2);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        var evidence1 = world.RestOrderingIntervals
            .Where(interval => interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
            .ToArray();

        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        world.ReplaceRestOrderingIntervals(evidence1);
        world.CredentialsUnavailable = true;
        var changedBefore = world.Events.OfType<PositionChangedEventV1>().Count();

        var delayed = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition(size: 2m)));

        delayed.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        delayed.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationUnavailable);
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.Events.OfType<PositionChangedEventV1>().Should().HaveCount(changedBefore);
    }

    [Fact]
    public async Task Older_balance_evidence_does_not_allow_silent_stale_after_newer_rest_evidence_is_lost()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []);
        world.Clock.UtcNow = T0.AddSeconds(1);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(2);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        var evidence1 = world.RestOrderingIntervals
            .Where(interval => interval.Kind == ExchangeRestOrderingKind.Balance)
            .ToArray();
        evidence1.Should().ContainSingle();
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(1_000m);

        world.Provider.Balance = new AccountBalance(AccountType.Unified, 3_000m, 2_900m, 2_500m, 100m, []);
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync2 = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync2.BalanceReadCompletedAt.Should().Be(T0.AddSeconds(20));
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(3_000m);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(T0.AddSeconds(20));
        world.Account.LastAppliedPositionsObservationAt.Should().NotBeNull();

        // Балансовое evidence REST №1 сохраняется; evidence REST №2 удаляется.
        world.ReplaceRestOrderingIntervals(evidence1);
        world.RestOrderingIntervals.Should().ContainSingle(interval =>
            interval.Kind == ExchangeRestOrderingKind.Balance &&
            interval.CompletedAt == T0.AddSeconds(2));
        var balanceWatermark = world.Account.LastAppliedBalanceObservationAt;
        var eventsBefore = world.Events.Count;

        var wallet = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.WalletObservation(T0.AddSeconds(15), totalEquity: 2_000m));

        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        wallet.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(3_000m);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(balanceWatermark);
        world.Events.Count.Should().Be(eventsBefore);
    }

    [Fact]
    public async Task Fresh_ws_after_accepted_rest_evidence_still_applies_directly()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete &&
            interval.StartedAt == T0.AddSeconds(10) &&
            interval.CompletedAt == T0.AddSeconds(20));

        var fresh = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(25), sequence: 1, OpenPosition(size: 2m)));

        fresh.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        fresh.ConfirmedByRest.Should().BeFalse();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
    }

    [Fact]
    public async Task Duplicate_ws_after_partial_evidence_loss_remains_duplicate()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(1);
        world.Clock.UtcNow = T0.AddSeconds(1);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(2);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        var evidence1 = world.RestOrderingIntervals
            .Where(interval => interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
            .ToArray();

        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        var first = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(25), sequence: 5, OpenPosition(size: 4m)));
        first.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        first.ConfirmedByRest.Should().BeFalse();
        world.ReplaceRestOrderingIntervals(evidence1);

        var duplicate = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(25), sequence: 5, OpenPosition(size: 4m)));

        duplicate.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(4m);
    }

    [Fact]
    public async Task Proven_older_wallet_with_accepted_balance_evidence_remains_stale()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Provider.Balance = new AccountBalance(AccountType.Unified, 3_000m, 2_900m, 2_500m, 100m, []);
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.Balance &&
            interval.CompletedAt == T0.AddSeconds(20));
        var equity = world.LatestPortfolio!.Capital.TotalEquity;
        var watermark = world.Account.LastAppliedBalanceObservationAt;

        var older = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.WalletObservation(T0.AddSeconds(5), totalEquity: 1_000m));

        older.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        older.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanBalance);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(equity);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(watermark);
    }

    [Fact]
    public async Task Initial_ws_without_prior_rest_still_applies_directly()
    {
        var world = new RealtimeTestWorld();
        world.Account.LastAppliedPositionsObservationAt.Should().BeNull();
        world.RestOrderingIntervals.Should().BeEmpty();

        var opened = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(5), sequence: 1, OpenPosition(size: 2m)));

        opened.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        opened.ConfirmedByRest.Should().BeFalse();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
    }

    [Fact]
    public async Task Missing_rest_evidence_does_not_allow_delayed_ws_position_rollback()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullPositionsCompleteAccepted.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.ReplaceRestOrderingIntervals();

        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Provider.SnapshotObservedAt = T0.AddMinutes(2);
        world.Clock.UtcNow = T0.AddMinutes(2);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        var delayed = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition(size: 2m)));

        delayed.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        delayed.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_rest_evidence_requires_recovery_when_confirmation_unavailable()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 3m));
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        world.ReplaceRestOrderingIntervals();
        world.CredentialsUnavailable = true;

        var delayed = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition(size: 2m)));

        delayed.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        delayed.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationUnavailable);
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(3m);
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Fresh_ws_after_proven_rest_completion_applies_directly_without_evidence_overlap()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 1m));
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete &&
            interval.CompletedAt == T0.AddSeconds(20));

        var fresh = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.OpenObservation(T0.AddSeconds(25), sequence: 1, OpenPosition(size: 2m)));

        fresh.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        fresh.ConfirmedByRest.Should().BeFalse();
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
    }

    [Fact]
    public async Task Missing_balance_evidence_does_not_silently_discard_ambiguous_wallet()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync.BalanceReadStartedAt.Should().Be(T0.AddSeconds(10));
        sync.BalanceReadCompletedAt.Should().Be(T0.AddSeconds(20));
        var acceptedEquity = world.LatestPortfolio!.Capital.TotalEquity;
        world.Account.LastAppliedPositionsObservationAt.Should().NotBeNull();
        world.ReplaceRestOrderingIntervals();

        var wallet = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.WalletObservation(T0.AddSeconds(15), totalEquity: 9_999m));

        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        wallet.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(acceptedEquity);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(T0.AddSeconds(20));
    }

    [Fact]
    public async Task Fresh_wallet_after_rest_applies_when_newer_than_accepted_capital()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        world.ReplaceRestOrderingIntervals();

        var wallet = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.WalletObservation(T0.AddSeconds(30), totalEquity: 3_500m));

        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(3_500m);
        world.LatestPortfolio.Capital.ObservedAt.Should().Be(T0.AddSeconds(30));
    }

    [Fact]
    public void Invalid_time_bounds_do_not_create_interval()
    {
        RestOrderingDecision.TryCreateBounds(T0.AddSeconds(10), T0.AddSeconds(5))
            .Should().BeNull();
        RestOrderingDecision.TryCreateBounds(T0, T0)
            .Should().Be((T0, T0));
    }

    [Fact]
    public async Task Sync_does_not_expose_inverted_positions_bounds_as_credible_read_window()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        // ObservedAt позже завершения чтения: достоверные границы не публикуются в результате.
        world.Provider.SnapshotObservedAt = T0.AddMinutes(10);
        world.Clock.UtcNow = T0.AddSeconds(1);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.AcceptedPositionsObservation.Should().BeTrue();
        result.PositionsReadStartedAt.Should().BeNull();
        result.PositionsReadCompletedAt.Should().BeNull();
        // Консервативная оболочка всё же сохраняется, чтобы не допустить DirectApply без защиты.
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete &&
            interval.StartedAt == T0.AddSeconds(1) &&
            interval.CompletedAt == T0.AddMinutes(10));
    }

    [Fact]
    public async Task Transaction_rollback_removes_evidence_mutations_and_outbox()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.CommitFailuresToInject = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId));

        world.RestOrderingIntervals.Should().BeEmpty();
        world.ActivePositions.Should().BeEmpty();
        world.Events.Should().BeEmpty();
        world.Portfolios.Should().BeEmpty();
    }

    [Fact]
    public async Task Rest_evidence_works_without_realtime_observation_state()
    {
        var world = new RealtimeTestWorld(observed: false);
        world.State.Should().BeNull();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(2);

        var result = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        world.State.Should().BeNull();
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete);
        world.RestOrderingIntervals.Should().Contain(interval =>
            interval.Kind == ExchangeRestOrderingKind.Balance);
    }

    [Fact]
    public async Task Successful_scoped_rest_confirmation_persists_scoped_evidence_atomically()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var before = world.RestOrderingIntervals.Count(interval =>
            interval.Kind == ExchangeRestOrderingKind.ScopedSymbol);

        world.Provider.ClearExchangePositions();
        world.Provider.SnapshotObservedAt = T0.AddMinutes(2);
        world.Clock.UtcNow = T0.AddMinutes(2);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(3);
        var close = await service.ApplyAsync(
            world.UserId,
            generation: 1,
            world.CloseObservation(T0.AddMinutes(1), sequence: 2));

        close.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        close.ConfirmedByRest.Should().BeTrue();
        world.RestOrderingIntervals.Count(interval => interval.Kind == ExchangeRestOrderingKind.ScopedSymbol)
            .Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Rejected_scoped_rest_does_not_create_success_evidence()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(1);
        await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        var before = world.RestOrderingIntervals
            .Count(interval => interval.Kind == ExchangeRestOrderingKind.ScopedSymbol);

        world.Provider.Status = OpenPositionsObservationStatus.Failed;
        var close = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            generation: 1,
            world.CloseObservation(T0.AddMinutes(2), sequence: 2));

        close.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.RestOrderingIntervals.Count(interval => interval.Kind == ExchangeRestOrderingKind.ScopedSymbol)
            .Should().Be(before);
    }

    private static ExchangeRestOrderingInterval Interval(
        ExchangeAccountId accountId,
        ExchangeRestOrderingKind kind,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? symbol = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExchangeAccountId = accountId,
            Kind = kind,
            Symbol = symbol,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            RecordedAt = completedAt,
        };
}
