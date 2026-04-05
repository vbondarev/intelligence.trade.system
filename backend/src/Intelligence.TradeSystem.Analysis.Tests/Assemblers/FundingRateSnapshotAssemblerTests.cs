using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Analysis.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

public sealed class FundingRateSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Entries_Is_Null()
    {
        var act = () => FundingRateSnapshotAssembler.Assemble(null!);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Throws_ArgumentException_When_Entries_Is_Empty()
    {
        var act = () => FundingRateSnapshotAssembler.Assemble([]);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Builds_Consistent_Snapshot_For_Unsorted_Deterministic_Series()
    {
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var newest = new[]
        {
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(168), fundingRate: 0.0012m),
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(160), fundingRate: 0.0006m),
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(152), fundingRate: 0.0000m),
        };

        var recent = Enumerable.Range(0, 18)
            .Select(i => FundingRateEntryFactory.Create(
                timestamp: baseTime.AddHours(144 - i * 8),
                fundingRate: 0.0006m))
            .ToArray();

        var oldest = FundingRateEntryFactory.Create(
            timestamp: baseTime,
            fundingRate: -0.005m);

        var entries = newest
            .Concat(recent)
            .Append(oldest)
            .Reverse() // намеренно ломаем порядок, newest не должен быть первым во входе
            .ToArray();

        var result = FundingRateSnapshotAssembler.Assemble(entries);

        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be(MarketCategory.Linear);

        result.WindowStartUtc.Should().Be(baseTime);
        result.WindowEndUtc.Should().Be(baseTime.AddHours(168));

        result.CurrentRate.Should().Be(0.0012m);
        result.Avg24hRate.Should().Be(0.0006m); // (0.0012 + 0.0006 + 0.0000) / 3
        result.Avg7dRate.Should().Be(0.0006m);  // (0.0012 + 0.0006 + 0.0000 + 18*0.0006) / 21 = 0.0006

        result.MaxRate.Should().Be(0.0012m);
        result.MinRate.Should().Be(-0.005m);

        result.IsPositive.Should().BeTrue();
        result.IsExtremeBullish.Should().BeTrue();
        result.IsExtremeBearish.Should().BeFalse();
    }

    [Fact]
    public void Uses_All_Available_Entries_When_Count_Is_Less_Than_Three_For_Avg24h_And_Less_Than_TwentyOne_For_Avg7d()
    {
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(16), fundingRate: 0.0003m),
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(8), fundingRate: 0.0009m),
        };

        var result = FundingRateSnapshotAssembler.Assemble(entries);

        result.Avg24hRate.Should().Be(0.0006m);
        result.Avg7dRate.Should().Be(0.0006m);
    }

    [Fact]
    public void Sets_IsPositive_When_CurrentRate_Is_Greater_Than_Zero_And_False_When_CurrentRate_Is_Zero()
    {
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var positive = FundingRateSnapshotAssembler.Assemble(
        [
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(8), fundingRate: 0.0001m),
            FundingRateEntryFactory.Create(timestamp: baseTime, fundingRate: -0.0002m),
        ]);

        var zero = FundingRateSnapshotAssembler.Assemble(
        [
            FundingRateEntryFactory.Create(timestamp: baseTime.AddHours(8), fundingRate: 0m),
            FundingRateEntryFactory.Create(timestamp: baseTime, fundingRate: -0.0002m),
        ]);

        positive.IsPositive.Should().BeTrue();
        zero.IsPositive.Should().BeFalse();
    }

    [Fact]
    public void Sets_IsExtremeBullish_When_CurrentRate_Is_Greater_Than_ExtremeThreshold()
    {
        var result = FundingRateSnapshotAssembler.Assemble(
        [
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), fundingRate: 0.0011m),
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), fundingRate: 0.0002m),
        ]);

        result.IsExtremeBullish.Should().BeTrue();
        result.IsExtremeBearish.Should().BeFalse();
    }

    [Fact]
    public void Sets_IsExtremeBearish_When_CurrentRate_Is_Less_Than_NegativeExtremeThreshold()
    {
        var result = FundingRateSnapshotAssembler.Assemble(
        [
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), fundingRate: -0.0011m),
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), fundingRate: 0.0002m),
        ]);

        result.IsExtremeBullish.Should().BeFalse();
        result.IsExtremeBearish.Should().BeTrue();
    }

    [Fact]
    public void Does_Not_Set_Extreme_Flags_When_CurrentRate_Equals_Positive_Or_Negative_ExtremeThreshold()
    {
        var positiveBoundary = FundingRateSnapshotAssembler.Assemble(
        [
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), fundingRate: 0.001m),
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), fundingRate: 0m),
        ]);

        var negativeBoundary = FundingRateSnapshotAssembler.Assemble(
        [
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), fundingRate: -0.001m),
            FundingRateEntryFactory.Create(timestamp: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), fundingRate: 0m),
        ]);

        positiveBoundary.IsExtremeBullish.Should().BeFalse();
        positiveBoundary.IsExtremeBearish.Should().BeFalse();

        negativeBoundary.IsExtremeBullish.Should().BeFalse();
        negativeBoundary.IsExtremeBearish.Should().BeFalse();
    }
}



