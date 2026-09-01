using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;
using Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Assemblers;

public sealed class LongShortRatioSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Entries_Is_Null()
    {
        var act = () => LongShortRatioSnapshotAssembler.Assemble(null!, LongShortRatioPeriod.OneHour);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Throws_ArgumentException_When_Entries_Is_Empty()
    {
        var act = () => LongShortRatioSnapshotAssembler.Assemble([], LongShortRatioPeriod.OneHour);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Builds_Consistent_Snapshot_For_Unsorted_Deterministic_Series()
    {
        var baseTime = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            LongShortRatioEntryFactory.Create(timestamp: baseTime.AddHours(2), buyRatio: 0.66m, sellRatio: 0.40m),
            LongShortRatioEntryFactory.Create(timestamp: baseTime, buyRatio: 0.30m, sellRatio: 0.80m),
            LongShortRatioEntryFactory.Create(timestamp: baseTime.AddHours(4), buyRatio: 0.34m, sellRatio: 0.20m),
            LongShortRatioEntryFactory.Create(timestamp: baseTime.AddHours(3), buyRatio: 0.50m, sellRatio: 0.50m),
        };

        var result = LongShortRatioSnapshotAssembler.Assemble(entries, LongShortRatioPeriod.OneHour);

        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be(MarketCategory.Linear);
        result.Period.Should().Be(LongShortRatioPeriod.OneHour);

        result.WindowStartUtc.Should().Be(baseTime);
        result.WindowEndUtc.Should().Be(baseTime.AddHours(4));

        result.CurrentBuyRatio.Should().Be(0.34m);
        result.CurrentSellRatio.Should().Be(0.20m);

        result.AvgBuyRatio.Should().Be(0.45m);
        result.AvgSellRatio.Should().Be(0.475m);

        result.IsLongDominant.Should().BeFalse();
        result.IsExtremelyLong.Should().BeFalse();
        result.IsExtremelyShort.Should().BeTrue();
    }

    [Fact]
    public void Sets_IsExtremelyLong_And_IsLongDominant_When_CurrentBuyRatio_Is_Greater_Than_Thresholds()
    {
        var result = LongShortRatioSnapshotAssembler.Assemble(
        [
            LongShortRatioEntryFactory.Create(
                timestamp: new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
                buyRatio: 0.66m,
                sellRatio: 0.10m),
            LongShortRatioEntryFactory.Create(
                timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero),
                buyRatio: 0.40m,
                sellRatio: 0.60m),
        ],
        LongShortRatioPeriod.OneHour);

        result.IsLongDominant.Should().BeTrue();
        result.IsExtremelyLong.Should().BeTrue();
        result.IsExtremelyShort.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.50, 0.10, false, false, false)]
    [InlineData(0.65, 0.10, true, false, false)]
    [InlineData(0.35, 0.10, false, false, false)]
    public void Uses_Strict_Boundaries_For_Dominance_And_Extreme_Flags(
        decimal currentBuyRatio,
        decimal currentSellRatio,
        bool expectedLongDominant,
        bool expectedExtremelyLong,
        bool expectedExtremelyShort)
    {
        var result = LongShortRatioSnapshotAssembler.Assemble(
        [
            LongShortRatioEntryFactory.Create(
                timestamp: new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
                buyRatio: currentBuyRatio,
                sellRatio: currentSellRatio),
            LongShortRatioEntryFactory.Create(
                timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero),
                buyRatio: 0.40m,
                sellRatio: 0.60m),
        ],
        LongShortRatioPeriod.OneHour);

        result.IsLongDominant.Should().Be(expectedLongDominant);
        result.IsExtremelyLong.Should().Be(expectedExtremelyLong);
        result.IsExtremelyShort.Should().Be(expectedExtremelyShort);
    }

    [Fact]
    public void Uses_CurrentBuyRatio_Not_CurrentSellRatio_To_Detect_ExtremelyShort()
    {
        var result = LongShortRatioSnapshotAssembler.Assemble(
        [
            LongShortRatioEntryFactory.Create(
                timestamp: new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
                buyRatio: 0.34m,
                sellRatio: 0.20m),
            LongShortRatioEntryFactory.Create(
                timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero),
                buyRatio: 0.60m,
                sellRatio: 0.40m),
        ],
        LongShortRatioPeriod.OneHour);

        result.IsExtremelyShort.Should().BeTrue();
    }
}
