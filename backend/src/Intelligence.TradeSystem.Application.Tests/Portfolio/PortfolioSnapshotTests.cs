using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PortfolioSnapshotTests
{
    [Fact]
    public void Unavailable_Is_Not_Available_And_Has_Zeroed_Numeric_Fields()
    {
        var snapshot = PortfolioSnapshot.Unavailable;

        snapshot.IsAvailable.Should().BeFalse();
        snapshot.TotalEquityUsd.Should().Be(0m);
        snapshot.AvailableBalanceUsd.Should().Be(0m);
        snapshot.TotalWalletBalanceUsd.Should().Be(0m);
        snapshot.TotalUnrealizedPnlUsd.Should().Be(0m);
        snapshot.OpenPositions.Should().BeEmpty();
    }

    [Fact]
    public void Unavailable_Returns_Independent_Instances_On_Each_Access()
    {
        var first = PortfolioSnapshot.Unavailable;
        var second = PortfolioSnapshot.Unavailable;

        first.Should().NotBeSameAs(second);
        first.OpenPositions.Should().NotBeSameAs(second.OpenPositions);
    }

    [Fact]
    public void Mutating_OpenPositions_Of_One_Unavailable_Instance_Does_Not_Affect_Another()
    {
        var first = PortfolioSnapshot.Unavailable;
        var second = PortfolioSnapshot.Unavailable;

        first.OpenPositions.Add(new OpenPositionSnapshot
        {
            Symbol = "BTCUSDT",
            Side = PositionSide.Long,
            Size = 1m,
            AvgPrice = 100m,
            MarkPrice = 100m,
            BreakEvenPrice = 100m,
            LiquidationPrice = 80m,
            PositionValueUsd = 100m,
            Leverage = 1m,
            UnrealizedPnlUsd = 0m,
            UnrealizedPnlPct = 0m,
        });

        first.OpenPositions.Should().HaveCount(1);
        second.OpenPositions.Should().BeEmpty();
    }
}
