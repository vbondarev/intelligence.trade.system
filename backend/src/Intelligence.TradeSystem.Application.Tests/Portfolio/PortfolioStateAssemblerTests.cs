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
    }
}
