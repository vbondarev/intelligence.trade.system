using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;
using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class EmaCalculatorTests
{
    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Throws_ArgumentNullException_When_Values_Is_Null()
    {
        var act = () => EmaCalculator.Compute(null!, period: 5);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("values");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Throws_ArgumentOutOfRangeException_When_Period_Is_Not_Positive(int period)
    {
        var act = () => EmaCalculator.Compute([10m, 20m, 30m], period);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(period));
    }

    // ── Boundary & fallback ──────────────────────────────────────────────────

    [Fact]
    public void Returns_Unavailable_When_Array_Is_Empty()
    {
        var result = EmaCalculator.Compute([], period: 5);

        result.Value.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.EmptyInput);
    }

    [Fact]
    public void Returns_Fallback_Average_When_Count_Less_Than_Period()
    {
        var result = EmaCalculator.Compute([1m, 2m, 3m], period: 10);

        result.Value.Should().Be(2m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Reason.Should().Be(IndicatorValueReason.PartialWindow);
    }

    [Fact]
    public void Returns_Fallback_Single_Value_When_Array_Has_One_Element()
    {
        // values.Length(1) < period(10) → Fallback: среднее по доступным = само значение.
        var result = EmaCalculator.Compute([42m], period: 10);

        result.Value.Should().BeApproximately(42m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Reason.Should().Be(IndicatorValueReason.PartialWindow);
    }

    [Fact]
    public void Returns_Available_SmaSeed_When_Count_Equals_Period()
    {
        var result = EmaCalculator.Compute([2m, 4m, 6m], period: 3);

        result.Value.Should().Be(4m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Last_Value_When_Period_Is_One()
    {
        // period=1 → k=1 → EMA вырождается в последнее значение
        var result = EmaCalculator.Compute([10m, 20m, 30m, 40m], period: 1);

        result.Value.Should().Be(40m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    // ── Smoothing formula ────────────────────────────────────────────────────

    [Fact]
    public void Uses_Classic_Smoothing_Formula_For_Known_Series()
    {
        // period=3 → k = 2/(3+1) = 0.5
        // Seed = (10+20+30)/3 = 20
        // i=3: EMA = 40×0.5 + 20×0.5 = 30
        // i=4: EMA = 50×0.5 + 30×0.5 = 40
        decimal[] values = [10m, 20m, 30m, 40m, 50m];

        var result = EmaCalculator.Compute(values, period: 3);

        result.Value.Should().BeApproximately(40m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Ema_For_Long_Deterministic_Series()
    {
        // period=4 → k = 2/(4+1) = 0.4
        // Seed = (10+20+30+40)/4 = 25
        // i=4: EMA = 25×0.4 + 25×0.6 = 25
        // i=5: EMA = 35×0.4 + 25×0.6 = 29
        // i=6: EMA = 45×0.4 + 29×0.6 = 35.4
        decimal[] values = [10m, 20m, 30m, 40m, 25m, 35m, 45m];

        var result = EmaCalculator.Compute(values, period: 4);

        result.Value.Should().BeApproximately(35.4m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Ema_For_Extended_Deterministic_Series()
    {
        // 12 значений с колебательным паттерном, period=3 → k = 0.5
        // Seed = (20+40+60)/3 = 40 → ... → i=11: 51.09375
        decimal[] values = [20m, 40m, 60m, 80m, 60m, 40m, 20m, 40m, 60m, 80m, 60m, 40m];

        var result = EmaCalculator.Compute(values, period: 3);

        result.Value.Should().BeApproximately(51.09375m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
    }

    // ── EMA vs SMA behavioral ────────────────────────────────────────────────

    [Fact]
    public void Gives_More_Weight_To_Recent_Values_In_Rising_Series()
    {
        // Серия: 15 значений = 10 (flat), затем резкий скачок до 100.
        // EMA реагирует быстрее SMA: EMA > SMA того же периода после скачка.
        var values = Enumerable.Repeat(10m, 15).Append(100m).ToArray();
        var period = 5;

        var ema = EmaCalculator.Compute(values, period).RequireValue();
        var sma = SmaCalculator.Compute(values, period).RequireValue();

        ema.Should().BeGreaterThan(sma);
    }

    [Fact]
    public void Reacts_Faster_Than_Sma_In_Falling_Series()
    {
        var values = Enumerable.Repeat(100m, 15).Append(10m).ToArray();
        var period = 5;

        var ema = EmaCalculator.Compute(values, period).RequireValue();
        var sma = SmaCalculator.Compute(values, period).RequireValue();

        ema.Should().BeLessThan(sma);
    }

    [Fact]
    public void Returns_Exact_Ema_After_Sharp_Impulse()
    {
        // 15×10m + 100m, period=5 → EMA ≈ 40
        var values = Enumerable.Repeat(10m, 15).Append(100m).ToArray();

        var result = EmaCalculator.Compute(values, period: 5);

        result.Value.Should().BeApproximately(40m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
    }

    // ── Flat series invariant ─────────────────────────────────────────────────

    [Theory]
    [InlineData(50, 30, 10)]  // 30 значений × 50m,  period=10
    [InlineData(75, 20, 3)]  // 20 значений × 75m,  period=3
    [InlineData(100, 15, 5)]  // 15 значений × 100m, period=5
    public void Returns_Available_Constant_For_Flat_Series(decimal constant, int count, int period)
    {
        // Для flat-серии EMA инициализируется через SMA константы и остаётся константой на всех шагах.
        var values = Enumerable.Repeat(constant, count).ToArray();

        var result = EmaCalculator.Compute(values, period);

        result.Value.Should().BeApproximately(constant, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }
}
