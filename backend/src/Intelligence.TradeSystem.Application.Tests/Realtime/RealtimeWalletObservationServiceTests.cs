using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class RealtimeWalletObservationServiceTests
{
    [Fact]
    public async Task Complete_Unified_Balance_Updates_Capital_And_Balance_Watermark_Only()
    {
        var world = new RealtimeTestWorld();
        world.SeedPosition(Position.Create(
            ExchangePositionKey.Create(world.AccountId, InstrumentId.From("BTCUSDT"), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            T0,
            T0));
        var service = world.CreateWalletService();
        var observedAt = T0.AddSeconds(5);

        var result = await service.ApplyAsync(world.UserId, 1, world.WalletObservation(observedAt));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        var capital = world.Portfolios.Should().ContainSingle().Subject.Capital;
        capital.TotalEquity.Should().Be(1_000m);
        capital.AvailableCapital.Should().Be(800m);
        capital.TotalWalletBalance.Should().Be(950m);
        capital.AccountUnrealizedPnl.Should().Be(50m);
        capital.ObservedAt.Should().Be(observedAt);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(observedAt);
        world.Account.LastAppliedPositionsObservationAt.Should().BeNull();
        world.ActivePositions.Single().LastObservedAt.Should().Be(T0);
        world.Events.Should().ContainSingle().Which.Should().BeOfType<PortfolioUpdatedEventV1>();
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task Missing_Value_Is_Not_Replaced_With_Zero(
        bool missingEquity,
        bool missingAvailable,
        bool missingWallet,
        bool missingPnl)
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(
                T0,
                totalEquity: missingEquity ? null : 1_000m,
                available: missingAvailable ? null : 800m,
                walletBalance: missingWallet ? null : 950m,
                unrealizedPnl: missingPnl ? null : 50m));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.WalletIncomplete);
        world.TransactionAttempts.Should().Be(0);
        world.Portfolios.Should().BeEmpty();
        world.Account.LastAppliedBalanceObservationAt.Should().BeNull();
    }

    [Fact]
    public async Task Unconfirmed_Wallet_Observation_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0) with { Kind = PrivateWalletObservationKind.Unconfirmed });

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.WalletUnconfirmed);
        world.Portfolios.Should().BeEmpty();
    }

    [Fact]
    public async Task Older_Or_Equal_Balance_Observation_Does_Not_Overwrite_Capital()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(10)));
        var balanceWatermark = world.Account.LastAppliedBalanceObservationAt;

        var older = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(5), totalEquity: 1_100m));
        var same = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity: 1_000.00m));
        var conflicting = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity: 1_200m));

        older.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        older.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanBalance);
        same.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        conflicting.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        conflicting.ReasonCode.Should().Be(RealtimeObservationApplyResult.BalanceConflict);
        world.Portfolios.Should().ContainSingle().Which.Capital.TotalEquity.Should().Be(1_000m);
        world.Account.LastAppliedBalanceObservationAt.Should().Be(balanceWatermark);
        world.Events.Should().ContainSingle();
    }

    [Theory]
    [InlineData(1_001, 800, 950, 50)]
    [InlineData(1_000, 801, 950, 50)]
    [InlineData(1_000, 800, 951, 50)]
    [InlineData(1_000, 800, 950, 49)]
    public async Task Equal_Time_With_Any_Different_Capital_Value_Requires_Recovery_Without_Writes(
        int totalEquity,
        int available,
        int walletBalance,
        int unrealizedPnl)
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(10)));
        var transactions = world.TransactionAttempts;

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity, available, walletBalance, unrealizedPnl));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.BalanceConflict);
        world.TransactionAttempts.Should().Be(transactions + 1);
        var capital = world.Portfolios.Should().ContainSingle().Subject.Capital;
        capital.TotalEquity.Should().Be(1_000m);
        capital.AvailableCapital.Should().Be(800m);
        capital.TotalWalletBalance.Should().Be(950m);
        capital.AccountUnrealizedPnl.Should().Be(50m);
        world.Events.Should().ContainSingle();

        var newer = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(11), totalEquity, available, walletBalance, unrealizedPnl));

        newer.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(totalEquity);
        world.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task Observations_Within_One_Stored_Microsecond_Are_Equal_Time()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0.AddSeconds(10).AddTicks(1)));

        var repeated = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10).AddTicks(5)));
        var conflicting = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10).AddTicks(9), totalEquity: 1_001m));

        repeated.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        conflicting.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        conflicting.ReasonCode.Should().Be(RealtimeObservationApplyResult.BalanceConflict);
        world.Portfolios.Should().ContainSingle();
        world.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Equal_Time_Values_Are_Compared_At_Stored_Capital_Precision()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity: 1_000.0000000000000000001m));

        var repeated = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(10), totalEquity: 1_000m));

        repeated.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.Portfolios.Should().ContainSingle();
        world.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Balance_Watermark_Advanced_By_Failed_Rest_Does_Not_Reject_Newer_Than_Applied_Capital()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();
        await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0));
        world.SetBalanceWatermark(T0.AddSeconds(10));

        var newer = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(5), totalEquity: 1_100m));
        var older = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(3), totalEquity: 1_050m));
        var same = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(5), totalEquity: 1_100m));

        newer.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        older.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        older.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanBalance);
        same.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.Portfolios.Should().HaveCount(2);
        world.Portfolios[^1].Capital.TotalEquity.Should().Be(1_100m);
        world.Portfolios[^1].Capital.ObservedAt.Should().Be(T0.AddSeconds(5));
        world.Account.LastAppliedBalanceObservationAt.Should().Be(T0.AddSeconds(10));
        world.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task Superseded_Generation_Rejects_Wallet_Observation()
    {
        var world = new RealtimeTestWorld();
        world.AdvanceGeneration();
        var service = world.CreateWalletService();

        var result = await service.ApplyAsync(world.UserId, 1, world.WalletObservation(T0));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.GenerationSuperseded);
        world.Portfolios.Should().BeEmpty();
        world.Account.LastAppliedBalanceObservationAt.Should().BeNull();
    }

    [Fact]
    public async Task Wallet_Observation_Without_Exchange_Time_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreateWalletService();

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0) with { MessageCreatedAt = null });

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.MissingObservationTime);
    }
}
