using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Diagnostics;
using Intelligence.TradeSystem.Indicators.Results;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Diagnostics;

public sealed class IndicatorDiagnosticFactoryTests
{
    // ── No diagnostic for normal available value ──────────────────────────────

    [Fact]
    public void Create_Returns_Null_When_Value_Is_Available_And_Not_Fallback()
    {
        var value = IndicatorValue.Available(100m);

        var result = IndicatorDiagnosticFactory.Create("1h", "ema20", value);

        result.Should().BeNull();
    }

    // ── Diagnostic for fallback value ─────────────────────────────────────────

    [Fact]
    public void Create_Returns_Diagnostic_When_Value_Is_Fallback()
    {
        var value = IndicatorValue.Fallback(100m, IndicatorValueReason.PartialWindow);

        var result = IndicatorDiagnosticFactory.Create("15m", "ema200", value);

        result.Should().NotBeNull();
        result!.Timeframe.Should().Be("15m");
        result.Indicator.Should().Be("ema200");
        result.Reason.Should().Be(IndicatorValueReason.PartialWindow);
        result.IsFallback.Should().BeTrue();
        result.Message.Should().Contain("15m.ema200");
        result.Message.Should().Contain("PartialWindow");
        result.Message.Should().Contain("fallback");
    }

    // ── Diagnostic for unavailable value ─────────────────────────────────────

    [Fact]
    public void Create_Returns_Diagnostic_When_Value_Is_Unavailable()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        var result = IndicatorDiagnosticFactory.Create("1h", "rsi14", value);

        result.Should().NotBeNull();
        result!.Timeframe.Should().Be("1h");
        result.Indicator.Should().Be("rsi14");
        result.Reason.Should().Be(IndicatorValueReason.InsufficientData);
        result.IsFallback.Should().BeFalse();
        result.Message.Should().Contain("unavailable");
        result.Message.Should().Contain("1h.rsi14");
        result.Message.Should().Contain("InsufficientData");
    }

    [Fact]
    public void Create_Returns_Diagnostic_When_Value_Is_Unavailable_With_EmptyInput_Reason()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.EmptyInput);

        var result = IndicatorDiagnosticFactory.Create("15m", "ema20", value);

        result.Should().NotBeNull();
        result!.Timeframe.Should().Be("15m");
        result.Indicator.Should().Be("ema20");
        result.Reason.Should().Be(IndicatorValueReason.EmptyInput);
        result.IsFallback.Should().BeFalse();
        result.Message.Should().Contain("unavailable");
        result.Message.Should().Contain("15m.ema20");
        result.Message.Should().Contain("EmptyInput");
    }

    [Fact]
    public void Create_Returns_Diagnostic_When_Value_Is_Unavailable_With_InvalidInput_Reason()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InvalidInput);

        var result = IndicatorDiagnosticFactory.Create("4h", "volumeRatio", value);

        result.Should().NotBeNull();
        result!.Timeframe.Should().Be("4h");
        result.Indicator.Should().Be("volumeRatio");
        result.Reason.Should().Be(IndicatorValueReason.InvalidInput);
        result.IsFallback.Should().BeFalse();
        result.Message.Should().Contain("unavailable");
        result.Message.Should().Contain("4h.volumeRatio");
        result.Message.Should().Contain("InvalidInput");
    }

    // ── Message format ────────────────────────────────────────────────────────

    [Fact]
    public void Create_Fallback_Message_Follows_Expected_Format()
    {
        var value = IndicatorValue.Fallback(50m, IndicatorValueReason.PartialWindow);

        var result = IndicatorDiagnosticFactory.Create("4h", "atr14", value);

        result!.Message.Should().Be("4h.atr14 calculated using fallback: PartialWindow.");
    }

    [Fact]
    public void Create_Unavailable_Message_Follows_Expected_Format()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        var result = IndicatorDiagnosticFactory.Create("1d", "atr14", value);

        result!.Message.Should().Be("1d.atr14 unavailable: InsufficientData.");
    }

    // ── Null value ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_Throws_ArgumentNullException_When_Value_Is_Null()
    {
        var act = () => IndicatorDiagnosticFactory.Create("1h", "ema20", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Timeframe / Indicator propagation ────────────────────────────────────

    [Theory]
    [InlineData("15m", "ema20")]
    [InlineData("1h",  "rsi14")]
    [InlineData("4h",  "atr14")]
    [InlineData("1d",  "volumeSma20")]
    public void Create_Propagates_Timeframe_And_Indicator_Into_Diagnostic(string timeframe, string indicator)
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        var result = IndicatorDiagnosticFactory.Create(timeframe, indicator, value);

        result!.Timeframe.Should().Be(timeframe);
        result.Indicator.Should().Be(indicator);
    }
}

