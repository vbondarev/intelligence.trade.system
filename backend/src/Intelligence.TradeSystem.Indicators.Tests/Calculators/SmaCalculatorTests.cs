using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class SmaCalculatorTests
{
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
}

