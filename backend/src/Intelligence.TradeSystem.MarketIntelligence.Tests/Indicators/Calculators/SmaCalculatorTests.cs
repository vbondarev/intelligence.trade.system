using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Calculators;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Results;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Indicators.Calculators;

public sealed class SmaCalculatorTests
{
    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Throws_ArgumentNullException_When_Values_Is_Null()
    {
        var act = () => SmaCalculator.Compute(null!, period: 5);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("values");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Throws_ArgumentOutOfRangeException_When_Period_Is_Not_Positive(int period)
    {
        var act = () => SmaCalculator.Compute([10m, 20m, 30m], period);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(period));
    }

    // ── Boundary & fallback ──────────────────────────────────────────────────

    [Fact]
    public void Returns_Unavailable_When_Array_Is_Empty()
    {
        var result = SmaCalculator.Compute([], period: 5);

        result.Value.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.EmptyInput);
    }

    [Fact]
    public void Returns_Fallback_Average_When_Count_Less_Than_Period()
    {
        var result = SmaCalculator.Compute([1m, 2m, 3m], period: 10);

        result.Value.Should().Be(2m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Reason.Should().Be(IndicatorValueReason.PartialWindow);
    }

    [Fact]
    public void Returns_Available_Average_When_Count_Equals_Period()
    {
        var result = SmaCalculator.Compute([2m, 4m, 6m], period: 3);

        result.Value.Should().Be(4m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Average_Of_Last_N_Values_When_Count_Greater_Than_Period()
    {
        // last 3: [3m, 10m, 20m] → average = 11m
        var result = SmaCalculator.Compute([1m, 2m, 3m, 10m, 20m], period: 3);

        result.Value.Should().Be(11m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Last_Value_When_Period_Is_One()
    {
        var result = SmaCalculator.Compute([1m, 2m, 3m], period: 1);

        result.Value.Should().BeApproximately(3m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
    }

    // ── Single-element & signed-value regressions ────────────────────────────

    [Fact]
    public void Returns_Fallback_Single_Value_When_Array_Has_One_Element()
    {
        // Единственный элемент → count(1) < period(10) → Fallback
        var result = SmaCalculator.Compute([42m], period: 10);

        result.Value.Should().BeApproximately(42m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Reason.Should().Be(IndicatorValueReason.PartialWindow);
    }

    [Fact]
    public void Returns_Available_Average_For_Mixed_Signed_Values()
    {
        // (-10 + 0 + 10) / 3 = 0
        var result = SmaCalculator.Compute([-10m, 0m, 10m], period: 3);

        result.Value.Should().BeApproximately(0m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
    }

    // ── Invariant: flat series always equals the constant ────────────────────

    public static TheoryData<double, int, int> FlatSeriesCases => new()
    {
        {  50.0, 30, 10 },  // 30 значений × 50,  period=10
        {  75.0, 20,  3 },  // 20 значений × 75,  period=3
        { 100.0, 15,  5 },  // 15 значений × 100, period=5
        {   1.0,  1,  1 },  // граничный: 1 элемент, period=1 → exact-period Available
    };

    [Theory]
    [MemberData(nameof(FlatSeriesCases))]
    public void Returns_Available_Constant_For_Flat_Series(double constantD, int count, int period)
    {
        // SMA flat series: каждое скользящее окно состоит из одного и того же значения,
        // поэтому результат обязан точно совпадать с константой, а не быть приближённым.
        var constant = (decimal)constantD;
        var values = Enumerable.Repeat(constant, count).ToArray();

        var result = SmaCalculator.Compute(values, period);

        result.IsAvailable.Should().BeTrue();
        result.Value.Should().BeApproximately(constant, precision: 0.0001m);
    }

    // ── Window-selection regression ──────────────────────────────────────────

    [Fact]
    public void Uses_Only_Last_N_Values_In_Window()
    {
        // Correct → last 3: (1 + 2 + 3) / 3 = 2
        // Wrong   → first 3: (1000 + 1000 + 1) / 3 ≈ 667
        var result = SmaCalculator.Compute([1000m, 1000m, 1m, 2m, 3m], period: 3);

        result.Value.Should().BeApproximately(2m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
    }
}
