using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Diagnostics;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Results;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Diagnostics;

public sealed class IndicatorDiagnosticListExtensionsTests
{
    // ── Normal available value — no entry added ───────────────────────────────

    [Fact]
    public void AddIfNeeded_Does_Not_Add_For_Normal_Available_Value()
    {
        var diagnostics = new List<IndicatorDiagnostic>();
        var value = IndicatorValue.Available(100m);

        diagnostics.AddIfNeeded("1h", "ema20", value);

        diagnostics.Should().BeEmpty();
    }

    // ── Fallback value — one entry added ─────────────────────────────────────

    [Fact]
    public void AddIfNeeded_Adds_One_Entry_For_Fallback_Value()
    {
        var diagnostics = new List<IndicatorDiagnostic>();
        var value = IndicatorValue.Fallback(100m, IndicatorValueReason.PartialWindow);

        diagnostics.AddIfNeeded("15m", "ema200", value);

        diagnostics.Should().HaveCount(1);
        diagnostics[0].Indicator.Should().Be("ema200");
        diagnostics[0].IsFallback.Should().BeTrue();
    }

    // ── Unavailable value — one entry added ──────────────────────────────────

    [Fact]
    public void AddIfNeeded_Adds_One_Entry_For_Unavailable_Value()
    {
        var diagnostics = new List<IndicatorDiagnostic>();
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        diagnostics.AddIfNeeded("1h", "rsi14", value);

        diagnostics.Should().HaveCount(1);
        diagnostics[0].Indicator.Should().Be("rsi14");
        diagnostics[0].IsFallback.Should().BeFalse();
    }

    [Fact]
    public void AddIfNeeded_Adds_Entry_For_Unavailable_With_EmptyInput_Reason()
    {
        var diagnostics = new List<IndicatorDiagnostic>();
        var value = IndicatorValue.Unavailable(IndicatorValueReason.EmptyInput);

        diagnostics.AddIfNeeded("15m", "ema20", value);

        diagnostics.Should().HaveCount(1);
        diagnostics[0].Indicator.Should().Be("ema20");
        diagnostics[0].Reason.Should().Be(IndicatorValueReason.EmptyInput);
        diagnostics[0].IsFallback.Should().BeFalse();
    }

    // ── Multiple calls accumulate ─────────────────────────────────────────────

    [Fact]
    public void AddIfNeeded_Accumulates_Multiple_Entries()
    {
        var diagnostics = new List<IndicatorDiagnostic>();
        var available = IndicatorValue.Available(100m);
        var fallback = IndicatorValue.Fallback(50m, IndicatorValueReason.PartialWindow);
        var unavailable = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        diagnostics.AddIfNeeded("1h", "ema20", available);
        diagnostics.AddIfNeeded("1h", "ema200", fallback);
        diagnostics.AddIfNeeded("1h", "rsi14", unavailable);

        diagnostics.Should().HaveCount(2);
        diagnostics[0].Indicator.Should().Be("ema200");
        diagnostics[1].Indicator.Should().Be("rsi14");
    }

    // ── Null diagnostics list → ArgumentNullException ────────────────────────

    [Fact]
    public void AddIfNeeded_Throws_When_Diagnostics_List_Is_Null()
    {
        ICollection<IndicatorDiagnostic> diagnostics = null!;
        var value = IndicatorValue.Available(100m);

        var act = () => diagnostics.AddIfNeeded("1h", "ema20", value);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Null value → ArgumentNullException ───────────────────────────────────

    [Fact]
    public void AddIfNeeded_Throws_When_Value_Is_Null()
    {
        var diagnostics = new List<IndicatorDiagnostic>();

        var act = () => diagnostics.AddIfNeeded("1h", "ema20", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
