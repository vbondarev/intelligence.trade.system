using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Tests.Helpers;

namespace Intelligence.TradeSystem.Indicators.Tests;

public sealed class TimeframeSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentException_When_Klines_Is_Empty()
    {
        var act = () => TimeframeSnapshotAssembler.Assemble([], timeframe: "1h");

        act.Should().Throw<ArgumentException>()
           .WithParameterName("klines");
    }

    [Fact]
    public void LastCandle_Is_Newest_Even_When_Input_Is_Unsorted()
    {
        var klines   = KlineFactory.CreateSeries(count: 250).ToList();
        var expected = klines.Max(k => k.StartTime);

        klines.Reverse(); // намеренно переворачиваем порядок

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.LastCandle.OpenTimeUtc.UtcDateTime.Should().Be(expected);
    }

    [Fact]
    public void Timeframe_Is_Propagated_Without_Change()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        result.Timeframe.Should().Be("4h");
    }

    [Fact]
    public void Ema_Values_Are_NonZero_With_Sufficient_Data()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Ema20.Should().NotBe(0m);
        result.Ema50.Should().NotBe(0m);
        result.Ema200.Should().NotBe(0m);
    }

    [Fact]
    public void IsAboveEma20_Matches_Close_Greater_Than_Ema20()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.IsAboveEma20.Should().Be(result.LastCandle.Close > result.Ema20);
    }

    [Fact]
    public void IsAboveEma50_Matches_Close_Greater_Than_Ema50()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.IsAboveEma50.Should().Be(result.LastCandle.Close > result.Ema50);
    }

    [Fact]
    public void IsAboveEma200_Matches_Close_Greater_Than_Ema200()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.IsAboveEma200.Should().Be(result.LastCandle.Close > result.Ema200);
    }

    [Fact]
    public void EmaBullishAlignment_Reflects_Ema_Order()
    {
        var klines = KlineFactory.CreateSeries(count: 250, trend: SeriesTrend.Bullish);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.EmaBullishAlignment.Should().Be(
            result.Ema20 > result.Ema50 && result.Ema50 > result.Ema200);
    }

    [Fact]
    public void EmaBearishAlignment_Reflects_Ema_Order()
    {
        var klines = KlineFactory.CreateSeries(count: 250, trend: SeriesTrend.Bearish, startPrice: 300m);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.EmaBearishAlignment.Should().Be(
            result.Ema20 < result.Ema50 && result.Ema50 < result.Ema200);
    }

    [Fact]
    public void RsiOverbought_Is_True_When_Rsi_At_Least_70()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.RsiOverbought.Should().Be(result.Rsi14 >= 70m);
    }

    [Fact]
    public void RsiOversold_Is_True_When_Rsi_At_Most_30()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.RsiOversold.Should().Be(result.Rsi14 <= 30m);
    }

    [Fact]
    public void Support_Levels_Are_Below_Current_Price_When_Present()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        if (result.Support1 > 0m)
            result.Support1.Should().BeLessThan(result.LastCandle.Close);

        if (result.Support2 > 0m)
            result.Support2.Should().BeLessThan(result.LastCandle.Close);
    }

    [Fact]
    public void Resistance_Levels_Are_Above_Current_Price_When_Present()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        if (result.Resistance1 > 0m)
            result.Resistance1.Should().BeGreaterThan(result.LastCandle.Close);

        if (result.Resistance2 > 0m)
            result.Resistance2.Should().BeGreaterThan(result.LastCandle.Close);
    }

    [Fact]
    public void VolumeRatio_Is_Zero_When_All_Volumes_Are_Zero()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines   = Enumerable.Range(0, 25)
            .Select(i => KlineFactory.Create(volume: 0m, startTime: baseTime.AddHours(i)))
            .ToList();

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.VolumeRatio.Should().Be(0m);
    }
}

