using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;
using Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;
using Xunit;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Assemblers;

public sealed class PriceSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Ticker_Is_Null()
    {
        var act = () => PriceSnapshotAssembler.Assemble(null!);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("ticker");
    }

    [Fact]
    public void Maps_All_Raw_Price_Fields_Without_Change()
    {
        var ticker = TickerFactory.Create(
            lastPrice: 101.25m,
            markPrice: 101.10m,
            indexPrice: 101.05m,
            bidPrice: 101.00m,
            bidSize: 15.5m,
            askPrice: 101.50m,
            askSize: 12.25m,
            price24hChangePct: 0.0345m,
            high24h: 110m,
            low24h: 95m,
            volume24h: 12_345m,
            turnover24h: 1_234_567m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.LastPrice.Should().Be(101.25m);
        result.MarkPrice.Should().Be(101.10m);
        result.IndexPrice.Should().Be(101.05m);
        result.BidPrice.Should().Be(101.00m);
        result.BidSize.Should().Be(15.5m);
        result.AskPrice.Should().Be(101.50m);
        result.AskSize.Should().Be(12.25m);
        result.Price24hChangePct.Should().Be(0.0345m);
        result.High24h.Should().Be(110m);
        result.Low24h.Should().Be(95m);
        result.Volume24h.Should().Be(12_345m);
        result.Turnover24h.Should().Be(1_234_567m);
    }

    [Fact]
    public void Computes_SpreadAbs_As_Ask_Minus_Bid()
    {
        var ticker = TickerFactory.Create(bidPrice: 100m, askPrice: 101.5m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadAbs.Should().Be(1.5m);
    }

    [Fact]
    public void Computes_SpreadPct_From_MidPrice()
    {
        // Bid=99, Ask=101 → spread=2, mid=100 → spreadPct = 2/100*100 = 2.
        var ticker = TickerFactory.Create(
            lastPrice: 500m,
            markPrice: 700m,
            indexPrice: 900m,
            bidPrice: 99m,
            askPrice: 101m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadPct.Should().Be(2m);
    }

    [Fact]
    public void Rounds_SpreadPct_To_Four_Decimals()
    {
        // Bid=100, Ask=100.01 → spread=0.01, mid=100.005.
        // spreadPct = 0.01 / 100.005 * 100 = 0.009999500024... → round(4) = 0.01.
        var ticker = TickerFactory.Create(bidPrice: 100m, askPrice: 100.01m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadPct.Should().Be(0.01m);
    }

    [Fact]
    public void Returns_Zero_SpreadPct_When_MidPrice_Is_Zero()
    {
        var ticker = TickerFactory.Create(bidPrice: -1m, askPrice: 1m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadAbs.Should().Be(2m);
        result.SpreadPct.Should().Be(0m);
    }

    [Fact]
    public void Returns_Zero_SpreadPct_When_MidPrice_Is_Negative()
    {
        var ticker = TickerFactory.Create(bidPrice: -10m, askPrice: -6m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadAbs.Should().Be(4m);
        result.SpreadPct.Should().Be(0m);
    }

    [Fact]
    public void Returns_Zero_SpreadAbs_And_Zero_SpreadPct_When_Bid_Equals_Ask()
    {
        var ticker = TickerFactory.Create(bidPrice: 100m, askPrice: 100m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadAbs.Should().Be(0m);
        result.SpreadPct.Should().Be(0m);
    }

    [Fact]
    public void Uses_Bid_And_Ask_For_MidPrice_Not_LastPrice_Or_MarkPrice()
    {
        // Если assembler случайно использует LastPrice/MarkPrice вместо mid(Bid,Ask),
        // spreadPct станет не 2, а совершенно другим значением.
        var ticker = TickerFactory.Create(
            lastPrice: 1000m,
            markPrice: 2000m,
            indexPrice: 3000m,
            bidPrice: 99m,
            askPrice: 101m);

        var result = PriceSnapshotAssembler.Assemble(ticker);

        result.SpreadAbs.Should().Be(2m);
        result.SpreadPct.Should().Be(2m);
    }
}
