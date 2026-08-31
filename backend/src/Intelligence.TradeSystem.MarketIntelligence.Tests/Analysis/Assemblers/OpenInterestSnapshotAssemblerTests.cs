using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;
using Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Assemblers;

public sealed class OpenInterestSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_Entries_Is_Null()
    {
        var act = () => OpenInterestSnapshotAssembler.Assemble(null!, OpenInterestInterval.OneHour);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Throws_ArgumentException_When_Entries_Is_Empty()
    {
        var act = () => OpenInterestSnapshotAssembler.Assemble([], OpenInterestInterval.OneHour);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Builds_Consistent_Snapshot_For_Unsorted_Deterministic_Series()
    {
        var current = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-2.5), openInterest: 105m),    // peak
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-0.333333333333), openInterest: 79m), // trough at 11:40
            OpenInterestEntryFactory.Create(timestamp: current, openInterest: 101.25m),
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-1.05), openInterest: 100m),   // 10:57, closest to 11:00
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-0.933333333333), openInterest: 98m), // 11:04
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-4.133333333333), openInterest: 80m), // 07:52
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-3.966666666667), openInterest: 82m), // 08:02, closest to 08:00
        };

        var result = OpenInterestSnapshotAssembler.Assemble(entries, OpenInterestInterval.OneHour);

        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be(MarketCategory.Linear);
        result.Interval.Should().Be(OpenInterestInterval.OneHour);

        result.WindowStartUtc.Should().Be(current.AddHours(-4.133333333333));
        result.WindowEndUtc.Should().Be(current);
        result.CurrentOpenInterest.Should().Be(101.25m);

        result.PeakOpenInterest.Should().Be(105m);
        result.TroughOpenInterest.Should().Be(79m);

        result.Change1hPct.Should().Be(1.25m);
        result.Change4hPct.Should().Be(23.4756m);

        result.IsAccumulating.Should().BeTrue();
        result.IsDistributing.Should().BeFalse();
    }

    [Fact]
    public void Returns_Zero_Change1hPct_When_Closest_Past_OpenInterest_Is_Zero()
    {
        var current = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-4), openInterest: 50m),
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-1), openInterest: 0m),
            OpenInterestEntryFactory.Create(timestamp: current, openInterest: 100m),
        };

        var result = OpenInterestSnapshotAssembler.Assemble(entries, OpenInterestInterval.OneHour);

        result.Change1hPct.Should().Be(0m);
        result.IsAccumulating.Should().BeFalse();
        result.IsDistributing.Should().BeFalse();
    }

    [Fact]
    public void Rounds_Change1hPct_To_Four_Decimals()
    {
        var current = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-4), openInterest: 100m),
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-1), openInterest: 100m),
            OpenInterestEntryFactory.Create(timestamp: current, openInterest: 100.33335m),
        };

        var result = OpenInterestSnapshotAssembler.Assemble(entries, OpenInterestInterval.OneHour);

        result.Change1hPct.Should().Be(0.3334m);
    }

    [Fact]
    public void Sets_IsDistributing_When_Change1hPct_Is_Less_Than_Minus_1()
    {
        var current = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-4), openInterest: 120m),
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-1), openInterest: 100m),
            OpenInterestEntryFactory.Create(timestamp: current, openInterest: 98m),
        };

        var result = OpenInterestSnapshotAssembler.Assemble(entries, OpenInterestInterval.OneHour);

        result.Change1hPct.Should().Be(-2m);
        result.IsAccumulating.Should().BeFalse();
        result.IsDistributing.Should().BeTrue();
    }

    [Fact]
    public void Does_Not_Set_IsAccumulating_When_Change1hPct_Equals_1()
    {
        var current = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-4), openInterest: 90m),
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-1), openInterest: 100m),
            OpenInterestEntryFactory.Create(timestamp: current, openInterest: 101m),
        };

        var result = OpenInterestSnapshotAssembler.Assemble(entries, OpenInterestInterval.OneHour);

        result.Change1hPct.Should().Be(1m);
        result.IsAccumulating.Should().BeFalse();
        result.IsDistributing.Should().BeFalse();
    }

    [Fact]
    public void Does_Not_Set_IsDistributing_When_Change1hPct_Equals_Minus_1()
    {
        var current = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new[]
        {
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-4), openInterest: 90m),
            OpenInterestEntryFactory.Create(timestamp: current.AddHours(-1), openInterest: 100m),
            OpenInterestEntryFactory.Create(timestamp: current, openInterest: 99m),
        };

        var result = OpenInterestSnapshotAssembler.Assemble(entries, OpenInterestInterval.OneHour);

        result.Change1hPct.Should().Be(-1m);
        result.IsAccumulating.Should().BeFalse();
        result.IsDistributing.Should().BeFalse();
    }
}
