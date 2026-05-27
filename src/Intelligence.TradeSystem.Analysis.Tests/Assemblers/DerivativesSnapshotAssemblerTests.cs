using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Analysis.Tests.Helpers;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

public sealed class DerivativesSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Ticker_Is_Null()
    {
        var act = () => DerivativesSnapshotAssembler.Assemble(null!, fundingRate: null, openInterest: null, longShortRatio: null);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("ticker");
    }

    [Fact]
    public void Maps_Current_Derivatives_Values_From_Ticker_When_All_Fields_Are_Present()
    {
        var nextFundingTime = new DateTimeOffset(2024, 1, 2, 8, 0, 0, TimeSpan.Zero);
        var ticker = TickerFactory.Create()
            with
        {
            FundingRate = 0.0008m,
            NextFundingTimeUtc = nextFundingTime,
            OpenInterest = 1_500m,
            OpenInterestValue = 2_250_000m,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.FundingRate.Should().Be(0.0008m);
        result.NextFundingTimeUtc.Should().Be(nextFundingTime);
        result.OpenInterest.Should().Be(1_500m);
        result.OpenInterestValue.Should().Be(2_250_000m);
    }

    [Fact]
    public void Uses_Zero_Fallback_When_Ticker_Derivative_Fields_Are_Null()
    {
        var ticker = TickerFactory.Create()
            with
        {
            FundingRate = null,
            NextFundingTimeUtc = null,
            OpenInterest = null,
            OpenInterestValue = null,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.FundingRate.Should().Be(0m);
        result.NextFundingTimeUtc.Should().BeNull();
        result.OpenInterest.Should().Be(0m);
        result.OpenInterestValue.Should().Be(0m);
    }

    [Fact]
    public void Computes_PremiumVsIndexPct_From_MarkPrice_And_IndexPrice()
    {
        var ticker = TickerFactory.Create(markPrice: 102m, indexPrice: 100m);

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.PremiumVsIndexPct.Should().Be(2m);
    }

    [Fact]
    public void Rounds_PremiumVsIndexPct_To_Four_Decimals()
    {
        var ticker = TickerFactory.Create(markPrice: 100.33335m, indexPrice: 100m);

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.PremiumVsIndexPct.Should().Be(0.3334m);
    }

    [Fact]
    public void Returns_Null_PremiumVsIndexPct_When_IndexPrice_Is_Zero_Or_Negative()
    {
        var zeroIndexTicker = TickerFactory.Create(markPrice: 100m, indexPrice: 0m);
        var negativeIndexTicker = TickerFactory.Create(markPrice: 100m, indexPrice: -10m);

        var zeroIndexResult = DerivativesSnapshotAssembler.Assemble(zeroIndexTicker, fundingRate: null, openInterest: null, longShortRatio: null);
        var negativeIndexResult = DerivativesSnapshotAssembler.Assemble(negativeIndexTicker, fundingRate: null, openInterest: null, longShortRatio: null);

        zeroIndexResult.PremiumVsIndexPct.Should().BeNull();
        negativeIndexResult.PremiumVsIndexPct.Should().BeNull();
    }

    [Fact]
    public void Uses_IndexPrice_As_Denominator_Not_MarkPrice_Or_LastPrice()
    {
        // (130 - 100) / 100 * 100 = 30.
        // Если ошибочно делить на MarkPrice (=130) или LastPrice (=500), получится другой результат.
        var ticker = TickerFactory.Create(lastPrice: 500m, markPrice: 130m, indexPrice: 100m);

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.PremiumVsIndexPct.Should().Be(30m);
    }

    [Fact]
    public void Uses_FundingRateSnapshot_Avg24hRate_When_Snapshot_Is_Provided()
    {
        var ticker = TickerFactory.Create()
            with
        {
            FundingRate = 0.0003m,
        };

        var fundingRate = new FundingRateSnapshot
        {
            Avg24hRate = 0.0009m,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate, openInterest: null, longShortRatio: null);

        result.FundingRateAvg24h.Should().Be(0.0009m);
    }

    [Fact]
    public void Falls_Back_To_Current_Ticker_FundingRate_When_FundingRateSnapshot_Is_Null()
    {
        var ticker = TickerFactory.Create()
            with
        {
            FundingRate = 0.0004m,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.FundingRateAvg24h.Should().Be(0.0004m);
    }

    [Fact]
    public void Falls_Back_To_Zero_FundingRateAvg24h_When_FundingRateSnapshot_And_TickerFundingRate_Are_Both_Null()
    {
        var ticker = TickerFactory.Create()
            with
        {
            FundingRate = null,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.FundingRateAvg24h.Should().Be(0m);
    }

    [Fact]
    public void Uses_OpenInterest_Changes_And_Positioning_From_Snapshots_When_Provided()
    {
        var ticker = TickerFactory.Create();

        var openInterest = new OpenInterestSnapshot
        {
            Change1hPct = 12.5m,
            Change4hPct = -3.25m,
        };

        var longShortRatio = new LongShortRatioSnapshot
        {
            CurrentBuyRatio = 0.62m,
            CurrentSellRatio = 0.38m,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest, longShortRatio);

        result.OpenInterestChange1hPct.Should().Be(12.5m);
        result.OpenInterestChange4hPct.Should().Be(-3.25m);
        result.LongRatio.Should().Be(0.62m);
        result.ShortRatio.Should().Be(0.38m);
    }

    [Fact]
    public void Falls_Back_To_Zero_For_OpenInterest_Changes_And_Positioning_When_Snapshots_Are_Missing()
    {
        var ticker = TickerFactory.Create();

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate: null, openInterest: null, longShortRatio: null);

        result.OpenInterestChange1hPct.Should().Be(0m);
        result.OpenInterestChange4hPct.Should().Be(0m);
        result.LongRatio.Should().Be(0m);
        result.ShortRatio.Should().Be(0m);
    }

    [Fact]
    public void Builds_Consistent_DerivativesSnapshot_When_All_Sources_Are_Provided()
    {
        var nextFundingTime = new DateTimeOffset(2024, 1, 2, 8, 0, 0, TimeSpan.Zero);
        var ticker = TickerFactory.Create(markPrice: 105m, indexPrice: 100m)
            with
        {
            FundingRate = 0.0005m,
            NextFundingTimeUtc = nextFundingTime,
            OpenInterest = 2_000m,
            OpenInterestValue = 3_000_000m,
        };

        var fundingRate = new FundingRateSnapshot
        {
            Avg24hRate = 0.0007m,
        };

        var openInterest = new OpenInterestSnapshot
        {
            Change1hPct = 8.25m,
            Change4hPct = 15.75m,
        };

        var longShortRatio = new LongShortRatioSnapshot
        {
            CurrentBuyRatio = 0.58m,
            CurrentSellRatio = 0.42m,
        };

        var result = DerivativesSnapshotAssembler.Assemble(ticker, fundingRate, openInterest, longShortRatio);

        result.FundingRate.Should().Be(0.0005m);
        result.NextFundingTimeUtc.Should().Be(nextFundingTime);
        result.OpenInterest.Should().Be(2_000m);
        result.OpenInterestValue.Should().Be(3_000_000m);
        result.PremiumVsIndexPct.Should().Be(5m);
        result.FundingRateAvg24h.Should().Be(0.0007m);
        result.OpenInterestChange1hPct.Should().Be(8.25m);
        result.OpenInterestChange4hPct.Should().Be(15.75m);
        result.LongRatio.Should().Be(0.58m);
        result.ShortRatio.Should().Be(0.42m);
    }
}
