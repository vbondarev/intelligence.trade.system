using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Tests.Results;

public sealed class IndicatorValueTests
{
    // ── Available ────────────────────────────────────────────────────────────

    [Fact]
    public void Available_Returns_Available_NonFallback_Value()
    {
        var result = IndicatorValue.Available(42.5m);

        result.Value.Should().Be(42.5m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    // ── Fallback ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fallback_Returns_Available_Fallback_Value_With_Reason()
    {
        var result = IndicatorValue.Fallback(10m, IndicatorValueReason.PartialWindow);

        result.Value.Should().Be(10m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Reason.Should().Be(IndicatorValueReason.PartialWindow);
    }

    [Fact]
    public void Fallback_Throws_ArgumentException_When_Reason_Is_None()
    {
        var act = () => IndicatorValue.Fallback(10m, IndicatorValueReason.None);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }

    // ── Unavailable ──────────────────────────────────────────────────────────

    [Fact]
    public void Unavailable_Returns_Unavailable_Value_With_Reason()
    {
        var result = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        result.Value.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.InsufficientData);
    }

    [Fact]
    public void Unavailable_Throws_ArgumentException_When_Reason_Is_None()
    {
        var act = () => IndicatorValue.Unavailable(IndicatorValueReason.None);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }
}
