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
/// Package 1c: биржевой порядок wallet/position и защита от clock skew.
/// </summary>
public sealed class WalletExchangeOrderingTests
{
    [Fact]
    public async Task Older_Wallet_MessageCreatedAt_Does_Not_Roll_Back_Capital_When_ObservedAt_Looks_Newer()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        // T20 принят при clamp ObservedAt=T15 (ReceivedAt отстаёт от биржи).
        var accepted = world.WalletObservation(T0.AddSeconds(20), totalEquity: 1_000m) with
        {
            ReceivedAt = T0.AddSeconds(15),
        };
        (await service.ApplyAsync(world.UserId, 1, accepted)).Outcome
            .Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(20));
        world.LatestPortfolio!.Capital.ObservedAt.Should().Be(T0.AddSeconds(15));

        // T19 со «свежим» ObservedAt без биржевого cursor ошибочно применялся бы поверх T20.
        var older = world.WalletObservation(T0.AddSeconds(19), totalEquity: 1_500m) with
        {
            ReceivedAt = T0.AddSeconds(25),
        };
        var result = await service.ApplyAsync(world.UserId, 1, older);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanBalance);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(1_000m);
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(20));
        world.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Equal_MessageCreatedAt_With_Same_Values_Is_Duplicate()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(10)));

        var repeated = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10)) with { ReceivedAt = T0.AddSeconds(30) });

        repeated.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.Portfolios.Should().ContainSingle();
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(10));
    }

    [Fact]
    public async Task Equal_MessageCreatedAt_With_Different_Values_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(10)));

        var conflicting = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity: 1_200m) with
            {
                ReceivedAt = T0.AddSeconds(30),
            });

        conflicting.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        conflicting.ReasonCode.Should().Be(RealtimeObservationApplyResult.BalanceConflict);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(1_000m);
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(10));
    }

    [Fact]
    public async Task Newer_Wallet_With_Clock_Skew_ObservedAt_Behind_Capital_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(20), totalEquity: 1_000m));

        var skewed = world.WalletObservation(T0.AddSeconds(30), totalEquity: 1_100m) with
        {
            ReceivedAt = T0.AddSeconds(5),
        };
        var result = await service.ApplyAsync(world.UserId, 1, skewed);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservationOrder);
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(1_000m);
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(20));
    }

    [Fact]
    public async Task Wallet_Inside_Rest_Balance_Interval_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync.BalanceReadStartedAt.Should().NotBeNull();

        var wallet = await world.CreateWalletService().ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(15), totalEquity: 2_000m));

        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        wallet.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        world.LastAcceptedWalletMessageCreatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rest_Balance_Does_Not_Overwrite_Newer_Wallet_When_Capital_ObservedAt_Is_Clamped()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        var accepted = world.WalletObservation(T0.AddSeconds(30), totalEquity: 3_000m) with
        {
            ReceivedAt = T0.AddSeconds(10),
        };
        (await service.ApplyAsync(world.UserId, 1, accepted)).Outcome
            .Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.LatestPortfolio!.Capital.ObservedAt.Should().Be(T0.AddSeconds(10));
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(30));

        world.Provider.Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []);
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(15);
        world.Provider.OnBalanceRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);

        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);

        sync.BalanceReadStartedAt.Should().BeNull();
        world.Account.LastError.Should().Be("balance_ambiguous");
        world.LatestPortfolio!.Capital.TotalEquity.Should().Be(3_000m);
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(30));
    }

    [Fact]
    public async Task Missing_MessageCreatedAt_Does_Not_Create_Fictitious_Cursor()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0) with { MessageCreatedAt = null });

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.MissingObservationTime);
        world.LastAcceptedWalletMessageCreatedAt.Should().BeNull();
        world.Portfolios.Should().BeEmpty();
    }

    [Fact]
    public async Task Stale_And_Recovery_Do_Not_Advance_Wallet_Cursor()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(20)));

        var stale = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity: 1_500m));
        var recovery = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(20), totalEquity: 1_500m));

        stale.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        recovery.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.LastAcceptedWalletMessageCreatedAt.Should().Be(T0.AddSeconds(20));
    }

    [Fact]
    public async Task Position_Seq_Ahead_With_Rolled_Back_ObservedAt_Requires_Recovery_Not_Stale()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 10));

        var skewed = world.OpenObservation(T0.AddSeconds(11), 11, OpenPosition(size: 2m)) with
        {
            ReceivedAt = T0.AddSeconds(5),
        };
        var result = await service.ApplyAsync(world.UserId, 1, skewed);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservationOrder);
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(10);
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Older_Seq_Is_Stale_Despite_Later_ReceivedAt()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 10));

        var older = world.OpenObservation(T0.AddSeconds(5), 9, OpenPosition(size: 2m)) with
        {
            ReceivedAt = T0.AddSeconds(20),
        };
        var result = await service.ApplyAsync(world.UserId, 1, older);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderRevision);
        world.ActivePositions.Single().Size.Should().Be(1m);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(10);
    }

    [Fact]
    public async Task Conflicting_Seq_And_ProviderUpdatedAt_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 10));

        var conflicting = world.OpenObservation(T0.AddSeconds(20), 9, OpenPosition(size: 2m));
        var result = await service.ApplyAsync(world.UserId, 1, conflicting);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RevisionConflict);
        world.ActivePositions.Single().Size.Should().Be(1m);
    }

    [Fact]
    public async Task Same_Revision_Different_Fingerprint_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 10));

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0, 10, OpenPosition(size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RevisionConflict);
    }

    [Fact]
    public async Task Same_Revision_Same_Fingerprint_Is_Duplicate()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        var observation = world.OpenObservation(T0, 10);
        await service.ApplyAsync(world.UserId, 1, observation);

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            observation with { ReceivedAt = T0.AddSeconds(5) });

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(10);
    }

    [Fact]
    public async Task Independent_Symbols_Keep_Independent_Sequences()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 3));
        await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(1), 50, OpenPosition(symbol: "ETHUSDT")));

        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(3);
        world.Cursor("ETHUSDT")!.LastProviderSequence.Should().Be(50);
    }

    [Fact]
    public async Task Delayed_Ws_Overlapping_Rest_Requires_Confirmation_Without_False_PositionChanged()
    {
        var world = new RealtimeTestWorld();
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.OnPositionsRequest = () => world.Clock.UtcNow = T0.AddSeconds(20);
        var sync = await world.CreateSyncService().SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullPositionsCompleteAccepted.Should().BeTrue();

        // Биржевой факт внутри REST-окна, а clamp ObservedAt выводит его за пределы интервала.
        var delayed = world.OpenObservation(T0.AddSeconds(15), sequence: 1, OpenPosition(size: 1m)) with
        {
            ReceivedAt = T0.AddSeconds(25),
            MessageCreatedAt = T0.AddSeconds(15),
            ProviderUpdatedAt = T0.AddSeconds(15),
        };
        var result = await world.CreatePositionService().ApplyAsync(world.UserId, 1, delayed);

        result.Outcome.Should().BeOneOf(
            RealtimeObservationApplyOutcome.RecoveryRequired,
            RealtimeObservationApplyOutcome.Applied);
        if (result.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            result.ConfirmedByRest.Should().BeTrue();
        }

        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(2m);
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Equal_Normalized_ObservedAt_With_Different_Revisions_Uses_Exchange_Order()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        var first = world.OpenObservation(T0.AddSeconds(10), 10) with
        {
            ReceivedAt = T0.AddSeconds(10),
        };
        await service.ApplyAsync(world.UserId, 1, first);

        var second = world.OpenObservation(T0.AddSeconds(10), 11, OpenPosition(size: 2m)) with
        {
            ReceivedAt = T0.AddSeconds(10),
            MessageCreatedAt = T0.AddSeconds(10),
            ProviderUpdatedAt = T0.AddSeconds(10),
        };
        var result = await service.ApplyAsync(world.UserId, 1, second);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.ActivePositions.Single().Size.Should().Be(2m);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(11);
    }
}
