using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Tests.Results;

public sealed class IndicatorValueExtensionsTests
{
    // ── OrNull ────────────────────────────────────────────────────────────────

    [Fact]
    public void OrNull_Returns_Value_When_Value_Is_Available()
    {
        var value = IndicatorValue.Available(99m);

        value.OrNull().Should().Be(99m);
    }

    [Fact]
    public void OrNull_Returns_Value_When_Value_Is_Fallback()
    {
        var value = IndicatorValue.Fallback(33m, IndicatorValueReason.PartialWindow);

        value.OrNull().Should().Be(33m);
    }

    [Fact]
    public void OrNull_Returns_Null_When_Value_Is_Unavailable()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.EmptyInput);

        value.OrNull().Should().BeNull();
    }

    [Fact]
    public void OrNull_Throws_ArgumentNullException_When_Value_Is_Null()
    {
        var act = () => ((IndicatorValue)null!).OrNull();

        act.Should().Throw<ArgumentNullException>();
    }

    // ── RequireValue ──────────────────────────────────────────────────────────

    [Fact]
    public void RequireValue_Returns_Value_When_Value_Is_Available()
    {
        var value = IndicatorValue.Available(55m);

        value.RequireValue().Should().Be(55m);
    }

    [Fact]
    public void RequireValue_Returns_Value_When_Value_Is_Fallback()
    {
        // Fallback: IsAvailable = true → RequireValue должен вернуть значение
        var value = IndicatorValue.Fallback(33m, IndicatorValueReason.PartialWindow);

        value.RequireValue().Should().Be(33m);
    }

    [Fact]
    public void RequireValue_Throws_InvalidOperationException_When_Value_Is_Unavailable()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        var act = () => value.RequireValue();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*InsufficientData*");
    }

    [Fact]
    public void RequireValue_Throws_ArgumentNullException_When_Value_Is_Null()
    {
        var act = () => ((IndicatorValue)null!).RequireValue();

        act.Should().Throw<ArgumentNullException>();
    }

    // ── HasUsableValue ────────────────────────────────────────────────────────

    [Fact]
    public void HasUsableValue_Returns_True_When_Value_Is_Available()
    {
        var value = IndicatorValue.Available(10m);

        value.HasUsableValue().Should().BeTrue();
    }

    [Fact]
    public void HasUsableValue_Returns_True_When_Value_Is_Fallback()
    {
        var value = IndicatorValue.Fallback(5m, IndicatorValueReason.PartialWindow);

        value.HasUsableValue().Should().BeTrue();
    }

    [Fact]
    public void HasUsableValue_Returns_False_When_Value_Is_Unavailable()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.EmptyInput);

        value.HasUsableValue().Should().BeFalse();
    }

    [Fact]
    public void HasUsableValue_Returns_False_When_Value_Is_Null() => ((IndicatorValue?)null).HasUsableValue().Should().BeFalse();

    // ── ShouldReportDiagnostic ────────────────────────────────────────────────

    [Fact]
    public void ShouldReportDiagnostic_Returns_False_When_Value_Is_Available_And_Not_Fallback()
    {
        var value = IndicatorValue.Available(77m);

        value.ShouldReportDiagnostic().Should().BeFalse();
    }

    [Fact]
    public void ShouldReportDiagnostic_Returns_True_When_Value_Is_Fallback()
    {
        var value = IndicatorValue.Fallback(7m, IndicatorValueReason.PartialWindow);

        value.ShouldReportDiagnostic().Should().BeTrue();
    }

    [Fact]
    public void ShouldReportDiagnostic_Returns_True_When_Value_Is_Unavailable()
    {
        var value = IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);

        value.ShouldReportDiagnostic().Should().BeTrue();
    }

    [Fact]
    public void ShouldReportDiagnostic_Throws_ArgumentNullException_When_Value_Is_Null()
    {
        var act = () => ((IndicatorValue)null!).ShouldReportDiagnostic();

        act.Should().Throw<ArgumentNullException>();
    }
}
