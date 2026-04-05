using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

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
    public void Returns_Zero_When_Array_Is_Empty()
    {
        var result = SmaCalculator.Compute([], period: 5);

        result.Should().Be(0m);
    }

    [Fact]
    public void Returns_Average_Of_All_When_Count_Less_Than_Period()
    {
        var result = SmaCalculator.Compute([1m, 2m, 3m], period: 10);

        result.Should().BeApproximately(2m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Average_Of_Last_N_Values_When_Count_Greater_Than_Period()
    {
        // last 3: 3 + 10 + 20 = 33 / 3 = 11
        var result = SmaCalculator.Compute([1m, 2m, 3m, 10m, 20m], period: 3);

        result.Should().BeApproximately(11m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Last_Value_When_Period_Is_One()
    {
        var result = SmaCalculator.Compute([1m, 2m, 3m], period: 1);

        result.Should().BeApproximately(3m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Exact_Average_When_Count_Equals_Period()
    {
        var result = SmaCalculator.Compute([2m, 4m, 6m], period: 3);

        result.Should().BeApproximately(4m, precision: 0.0001m);
    }

    // ── Single-element & signed-value regressions ────────────────────────────

    [Fact]
    public void Returns_Single_Value_When_Array_Has_One_Element()
    {
        // Контракт: единственный элемент всегда возвращается как SMA,
        // независимо от того, насколько period больше длины массива.
        var result = SmaCalculator.Compute([42m], period: 10);

        result.Should().BeApproximately(42m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Expected_Average_For_Mixed_Signed_Values()
    {
        // Regression: SMA должна корректно работать с отрицательными значениями
        // (например, derived series: P&L, спред, отклонение от бенчмарка).
        // (-10 + 0 + 10) / 3 = 0
        var result = SmaCalculator.Compute([-10m, 0m, 10m], period: 3);

        result.Should().BeApproximately(0m, precision: 0.0001m);
    }

    // ── Window-selection regression ──────────────────────────────────────────

    [Fact]
    public void Uses_Only_Last_N_Values_In_Window()
    {
        // Ранние значения (1000, 1000) намеренно велики, чтобы тест упал,
        // если реализация случайно возьмёт первые элементы или неверное окно.
        //   Correct → last 3: (1 + 2 + 3) / 3 = 2
        //   Wrong → first 3: (1000 + 1000 + 1) / 3 ≈ 667
        var result = SmaCalculator.Compute([1000m, 1000m, 1m, 2m, 3m], period: 3);

        result.Should().BeApproximately(2m, precision: 0.0001m);
    }
}

