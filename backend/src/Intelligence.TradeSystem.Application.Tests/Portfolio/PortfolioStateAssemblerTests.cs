using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PortfolioStateAssemblerTests
{
    private static readonly ExchangeAccountId Account = ExchangeAccountId.New();
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Null_Balance_Produces_Unknown_Capital_Not_Zero()
    {
        var result = PortfolioStateAssembler.Assemble(
            null, null, [], Account, ObservedAt.AddMinutes(1), TimeSpan.FromMinutes(5));

        result.Capital.TotalEquity.Should().BeNull();
        result.Capital.AvailableCapital.Should().BeNull();
        result.Capital.ObservedAt.Should().BeNull();
        result.IsComplete.Should().BeFalse();
        result.IsFresh.Should().BeFalse();
    }

    [Fact]
    public void Maps_AccountBalance_To_PortfolioCapitalState()
    {
        var balance = new AccountBalance(
            AccountType.Unified, 12500m, 12000m, 8000m, 500m, []);
        var result = PortfolioStateAssembler.Assemble(
            balance, ObservedAt, [], Account, ObservedAt.AddMinutes(1), TimeSpan.FromMinutes(5));

        result.Capital.TotalEquity.Should().Be(12500m);
        result.Capital.AvailableCapital.Should().Be(8000m);
        result.Capital.TotalWalletBalance.Should().Be(12000m);
        result.Capital.ObservedAt.Should().Be(ObservedAt);
        result.Capital.AccountUnrealizedPnl.Should().Be(500m);
    }

    [Theory]
    [InlineData(-250.5)]
    [InlineData(0)]
    public void Maps_Negative_And_Zero_Account_Unrealized_Pnl(decimal pnl)
    {
        var balance = new AccountBalance(AccountType.Unified, 12500m, 12000m, 8000m, pnl, []);

        var capital = PortfolioStateAssembler.CreateCapital(balance, ObservedAt);

        capital.AccountUnrealizedPnl.Should().Be(pnl);
    }

    [Fact]
    public void Missing_Account_Unrealized_Pnl_Stays_Unknown()
    {
        var balance = new AccountBalance(AccountType.Unified, 12500m, 12000m, 8000m, null, []);

        var capital = PortfolioStateAssembler.CreateCapital(balance, ObservedAt);

        capital.AccountUnrealizedPnl.Should().BeNull();
    }

    [Fact]
    public void Missing_Balance_Leaves_Account_Unrealized_Pnl_Unknown()
    {
        var result = PortfolioStateAssembler.Assemble(
            null, null, [], Account, ObservedAt.AddMinutes(1), TimeSpan.FromMinutes(5));

        result.Capital.AccountUnrealizedPnl.Should().BeNull();
    }

    [Fact]
    public void Reuses_Previous_Capital_Without_Changing_Its_Observation_Time()
    {
        var previousCapital = new PortfolioCapitalState(
            12500m,
            8000m,
            ObservedAt,
            12000m);

        var result = PortfolioStateAssembler.AssembleWithCapital(
            previousCapital,
            [],
            Account,
            ObservedAt.AddMinutes(10),
            TimeSpan.FromMinutes(5));

        result.Capital.Should().BeSameAs(previousCapital);
        result.Capital.ObservedAt.Should().Be(ObservedAt);
        result.IsFresh.Should().BeFalse();
    }

    [Fact]
    public void Incomplete_Position_Coverage_Makes_Known_Capital_NotFresh_And_Incomplete()
    {
        var result = PortfolioStateAssembler.AssembleWithCapital(
            new PortfolioCapitalState(12500m, 8000m, ObservedAt, 12000m),
            [],
            Account,
            ObservedAt.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            positionsFullyReconciled: false);

        result.PositionsFullyReconciled.Should().BeFalse();
        result.IsFresh.Should().BeFalse();
        result.IsComplete.Should().BeFalse();
    }
}
