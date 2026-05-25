using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Analysis.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

public sealed class TradeFlowSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Trades_Is_Null()
    {
        var act = () => TradeFlowSnapshotAssembler.Assemble(null!);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("trades");
    }

    [Fact]
    public void Throws_ArgumentException_When_Trades_Is_Empty()
    {
        var act = () => TradeFlowSnapshotAssembler.Assemble([]);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("trades");
    }

    [Fact]
    public void Builds_Consistent_Snapshot_For_Mixed_Trade_Set_When_Input_Is_Unsorted()
    {
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);
        var t3 = t0.AddMinutes(3);

        var trades = new[]
        {
            TradeFactory.Create(timestamp: t2, side: TradeSide.Buy,  quantity: 2m, price: 101m),
            TradeFactory.Create(timestamp: t0, side: TradeSide.Sell, quantity: 4m, price:  99m),
            TradeFactory.Create(timestamp: t3, side: TradeSide.Buy,  quantity: 3m, price: 102m),
            TradeFactory.Create(timestamp: t1, side: TradeSide.Sell, quantity: 1m, price: 100m),
        };

        var result = TradeFlowSnapshotAssembler.Assemble(trades);

        result.WindowStartUtc.Should().Be(t0);
        result.WindowEndUtc.Should().Be(t3);

        result.BuyVolume.Should().Be(5m);
        result.SellVolume.Should().Be(5m);
        result.DeltaVolume.Should().Be(0m);
        result.DeltaPct.Should().Be(0m);

        result.TotalTrades.Should().Be(4);
        result.BuyTrades.Should().Be(2);
        result.SellTrades.Should().Be(2);

        result.AvgTradeSize.Should().Be(2.5m);
        result.MaxTradeSize.Should().Be(4m);

        result.HasAggressiveBuyPressure.Should().BeFalse();
        result.HasAggressiveSellPressure.Should().BeFalse();
    }

    [Fact]
    public void Returns_Zero_DeltaPct_And_AvgTradeSize_When_TotalVolume_Is_Zero()
    {
        var trades = new[]
        {
            TradeFactory.Create(side: TradeSide.Buy, quantity: 0m),
            TradeFactory.Create(side: TradeSide.Sell, quantity: 0m),
        };

        var result = TradeFlowSnapshotAssembler.Assemble(trades);

        result.BuyVolume.Should().Be(0m);
        result.SellVolume.Should().Be(0m);
        result.DeltaVolume.Should().Be(0m);
        result.DeltaPct.Should().Be(0m);
        result.AvgTradeSize.Should().Be(0m);
        result.MaxTradeSize.Should().Be(0m);
        result.HasAggressiveBuyPressure.Should().BeFalse();
        result.HasAggressiveSellPressure.Should().BeFalse();
    }

    [Fact]
    public void Sets_HasAggressiveBuyPressure_When_DeltaPct_Is_Greater_Than_10()
    {
        // Buy=11, Sell=4 → delta=7; total=15 → deltaPct = 46.6666...
        var trades = new[]
        {
            TradeFactory.Create(side: TradeSide.Buy, quantity: 6m),
            TradeFactory.Create(side: TradeSide.Buy, quantity: 5m),
            TradeFactory.Create(side: TradeSide.Sell, quantity: 4m),
        };

        var result = TradeFlowSnapshotAssembler.Assemble(trades);

        result.BuyVolume.Should().Be(11m);
        result.SellVolume.Should().Be(4m);
        result.DeltaVolume.Should().Be(7m);
        result.DeltaPct.Should().BeApproximately(46.6667m, 0.0001m);
        result.HasAggressiveBuyPressure.Should().BeTrue();
        result.HasAggressiveSellPressure.Should().BeFalse();
    }

    [Fact]
    public void Sets_HasAggressiveSellPressure_When_DeltaPct_Is_Less_Than_Minus_10()
    {
        // Buy=2, Sell=10 → delta=-8; total=12 → deltaPct = -66.6666...
        var trades = new[]
        {
            TradeFactory.Create(side: TradeSide.Buy, quantity: 2m),
            TradeFactory.Create(side: TradeSide.Sell, quantity: 4m),
            TradeFactory.Create(side: TradeSide.Sell, quantity: 6m),
        };

        var result = TradeFlowSnapshotAssembler.Assemble(trades);

        result.BuyVolume.Should().Be(2m);
        result.SellVolume.Should().Be(10m);
        result.DeltaVolume.Should().Be(-8m);
        result.DeltaPct.Should().BeApproximately(-66.6667m, 0.0001m);
        result.HasAggressiveBuyPressure.Should().BeFalse();
        result.HasAggressiveSellPressure.Should().BeTrue();
    }

    [Fact]
    public void Does_Not_Set_AggressiveBuyPressure_When_DeltaPct_Equals_10()
    {
        // Buy=11, Sell=9 → delta=2; total=20 → deltaPct = 10
        var trades = new[]
        {
            TradeFactory.Create(side: TradeSide.Buy, quantity: 11m),
            TradeFactory.Create(side: TradeSide.Sell, quantity: 9m),
        };

        var result = TradeFlowSnapshotAssembler.Assemble(trades);

        result.DeltaPct.Should().Be(10m);
        result.HasAggressiveBuyPressure.Should().BeFalse();
        result.HasAggressiveSellPressure.Should().BeFalse();
    }

    [Fact]
    public void Does_Not_Set_AggressiveSellPressure_When_DeltaPct_Equals_Minus_10()
    {
        // Buy=9, Sell=11 → delta=-2; total=20 → deltaPct = -10
        var trades = new[]
        {
            TradeFactory.Create(side: TradeSide.Buy, quantity: 9m),
            TradeFactory.Create(side: TradeSide.Sell, quantity: 11m),
        };

        var result = TradeFlowSnapshotAssembler.Assemble(trades);

        result.DeltaPct.Should().Be(-10m);
        result.HasAggressiveBuyPressure.Should().BeFalse();
        result.HasAggressiveSellPressure.Should().BeFalse();
    }
}
