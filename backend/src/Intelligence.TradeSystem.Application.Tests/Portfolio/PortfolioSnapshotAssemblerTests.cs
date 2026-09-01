using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PortfolioSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Positions_Is_Null()
    {
        var act = () => PortfolioSnapshotAssembler.Assemble(balance: null, positions: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("positions");
    }

    [Fact]
    public void Returns_Zero_Balance_Fields_And_Empty_OpenPositions_When_Balance_Is_Null()
    {
        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions: []);

        result.TotalEquityUsd.Should().Be(0m);
        result.AvailableBalanceUsd.Should().Be(0m);
        result.TotalWalletBalanceUsd.Should().Be(0m);
        result.TotalUnrealizedPnlUsd.Should().Be(0m);
        result.OpenPositions.Should().BeEmpty();
    }

    [Fact]
    public void Maps_Balance_Fields_From_AccountBalance()
    {
        var balance = CreateBalance(12_500.75m, 11_900.50m, 8_400.25m, 600.25m);

        var result = PortfolioSnapshotAssembler.Assemble(balance, positions: []);

        result.TotalEquityUsd.Should().Be(12_500.75m);
        result.AvailableBalanceUsd.Should().Be(8_400.25m);
        result.TotalWalletBalanceUsd.Should().Be(11_900.50m);
        result.TotalUnrealizedPnlUsd.Should().Be(600.25m);
    }

    [Fact]
    public void Skips_Positions_With_Zero_Or_Negative_Size()
    {
        var positions = new[]
        {
            CreatePosition(symbol: "BTCUSDT", size: 0m),
            CreatePosition(symbol: "ETHUSDT", size: -1m),
            CreatePosition(symbol: "SOLUSDT", size: 2m),
        };

        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions);

        result.OpenPositions.Should().HaveCount(1);
        result.OpenPositions[0].Symbol.Should().Be("SOLUSDT");
    }

    [Fact]
    public void Maps_Position_Fields_And_Preserves_Input_Order_For_Remaining_Open_Positions()
    {
        var positions = new[]
        {
            CreatePosition(symbol: "BTCUSDT", side: PositionSide.Long, size: 2m, avgPrice: 100m, positionValue: 250m, leverage: 5m, markPrice: 125m, breakEvenPrice: 101m, liquidationPrice: 80m, unrealizedPnl: 25m),
            CreatePosition(symbol: "ETHUSDT", side: PositionSide.Short, size: 3m, avgPrice: 200m, positionValue: 540m, leverage: 3m, markPrice: 180m, breakEvenPrice: 198m, liquidationPrice: 260m, unrealizedPnl: -60m),
        };

        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions);

        result.OpenPositions.Should().SatisfyRespectively(
            first =>
            {
                first.Symbol.Should().Be("BTCUSDT");
                first.Side.Should().Be(PositionSide.Long);
                first.Size.Should().Be(2m);
                first.AvgPrice.Should().Be(100m);
                first.MarkPrice.Should().Be(125m);
                first.BreakEvenPrice.Should().Be(101m);
                first.LiquidationPrice.Should().Be(80m);
                first.PositionValueUsd.Should().Be(250m);
                first.Leverage.Should().Be(5m);
                first.UnrealizedPnlUsd.Should().Be(25m);
                first.UnrealizedPnlPct.Should().Be(10m);
            },
            second =>
            {
                second.Symbol.Should().Be("ETHUSDT");
                second.Side.Should().Be(PositionSide.Short);
                second.Size.Should().Be(3m);
                second.AvgPrice.Should().Be(200m);
                second.MarkPrice.Should().Be(180m);
                second.BreakEvenPrice.Should().Be(198m);
                second.LiquidationPrice.Should().Be(260m);
                second.PositionValueUsd.Should().Be(540m);
                second.Leverage.Should().Be(3m);
                second.UnrealizedPnlUsd.Should().Be(-60m);
                second.UnrealizedPnlPct.Should().Be(-11.1111m);
            });
    }

    [Fact]
    public void Falls_Back_To_Zero_For_Null_Position_Fields()
    {
        var positions = new[]
        {
            CreatePosition(avgPrice: null, positionValue: null, leverage: null, markPrice: null, breakEvenPrice: null, liquidationPrice: null, unrealizedPnl: null),
        };

        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions);

        var position = result.OpenPositions.Should().ContainSingle().Subject;
        position.AvgPrice.Should().Be(0m);
        position.MarkPrice.Should().Be(0m);
        position.BreakEvenPrice.Should().Be(0m);
        position.LiquidationPrice.Should().Be(0m);
        position.PositionValueUsd.Should().Be(0m);
        position.Leverage.Should().Be(0m);
        position.UnrealizedPnlUsd.Should().Be(0m);
        position.UnrealizedPnlPct.Should().Be(0m);
    }

    [Fact]
    public void Calculates_UnrealizedPnlPct_From_PositionValue_And_Rounds_To_Four_Decimals()
    {
        var positions = new[] { CreatePosition(positionValue: 3m, unrealizedPnl: 1m) };

        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions);

        result.OpenPositions[0].UnrealizedPnlPct.Should().Be(33.3333m);
    }

    [Fact]
    public void Maps_Nullable_Balance_Fields_To_Zero_When_Individually_Null()
    {
        var balance = new AccountBalance(AccountType.Unified, null, null, null, null, []);

        var result = PortfolioSnapshotAssembler.Assemble(balance, positions: []);

        result.TotalEquityUsd.Should().Be(0m);
        result.AvailableBalanceUsd.Should().Be(0m);
        result.TotalWalletBalanceUsd.Should().Be(0m);
        result.TotalUnrealizedPnlUsd.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void Returns_Zero_UnrealizedPnlPct_When_PositionValue_Is_Zero_Or_Negative(decimal positionValue)
    {
        var positions = new[] { CreatePosition(positionValue: positionValue, unrealizedPnl: 25m) };

        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions);

        result.OpenPositions[0].UnrealizedPnlPct.Should().Be(0m);
        result.OpenPositions[0].PositionValueUsd.Should().Be(positionValue);
    }

    [Fact]
    public void Does_Not_Recompute_PositionValue_From_Size_Times_MarkPrice()
    {
        // Source PositionValue diverges from Size * MarkPrice (2 * 100 = 200) on purpose.
        var positions = new[] { CreatePosition(size: 2m, markPrice: 100m, positionValue: 999m) };

        var result = PortfolioSnapshotAssembler.Assemble(balance: null, positions);

        result.OpenPositions[0].PositionValueUsd.Should().Be(999m);
    }

    private static AccountBalance CreateBalance(decimal? totalEquity = 10_000m, decimal? totalWalletBalance = 9_500m, decimal? totalAvailableBalance = 7_000m, decimal? totalPerpUnrealizedPnl = 500m) =>
        new(AccountType.Unified, totalEquity, totalWalletBalance, totalAvailableBalance, totalPerpUnrealizedPnl, []);

    private static OpenPosition CreatePosition(
        string symbol = "BTCUSDT",
        MarketCategory category = MarketCategory.Linear,
        PositionSide side = PositionSide.Long,
        PositionStatus status = PositionStatus.Normal,
        decimal size = 1m,
        decimal? avgPrice = 100m,
        decimal? positionValue = 100m,
        decimal? leverage = 2m,
        decimal? markPrice = 100m,
        decimal? breakEvenPrice = 100m,
        decimal? liquidationPrice = 80m,
        decimal? unrealizedPnl = 0m,
        decimal? takeProfit = null,
        decimal? stopLoss = null,
        decimal? trailingStop = null,
        int riskId = 1,
        decimal? riskLimitValue = 100_000m,
        DateTimeOffset? createdTime = null,
        DateTimeOffset? updatedTime = null) =>
        new(symbol, category, side, status, size, avgPrice, positionValue, leverage, markPrice, breakEvenPrice, liquidationPrice, unrealizedPnl, takeProfit, stopLoss, trailingStop, riskId, riskLimitValue, createdTime, updatedTime);
}
