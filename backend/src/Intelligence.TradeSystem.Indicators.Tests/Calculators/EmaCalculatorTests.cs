using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class EmaCalculatorTests
{
    [Fact]
    public void Returns_Zero_When_Array_Is_Empty()
    {
        var result = EmaCalculator.Compute([], period: 10);

        result.Should().Be(0m);
    }

    [Fact]
    public void Returns_Average_Of_All_When_Count_Less_Than_Period()
    {
        var result = EmaCalculator.Compute([1m, 2m, 3m], period: 10);

        result.Should().BeApproximately(2m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Sma_Seed_When_Count_Equals_Period()
    {
        // Ровно period значений → loop не выполняется → результат = SMA seed
        var result = EmaCalculator.Compute([2m, 4m, 6m], period: 3);

        result.Should().BeApproximately(4m, precision: 0.0001m);
    }

    [Fact]
    public void Gives_More_Weight_To_Recent_Values_In_Rising_Series()
    {
        // Серия: 15 значений = 10 (flat), затем резкий скачок до 100.
        // EMA реагирует быстрее SMA: EMA > SMA того же периода после скачка.
        var values = Enumerable.Repeat(10m, 15).Append(100m).ToArray();
        var period = 5;

        var ema = EmaCalculator.Compute(values, period);
        var sma = SmaCalculator.Compute(values, period);

        ema.Should().BeGreaterThan(sma);
    }

    [Fact]
    public void Converges_To_Constant_For_Flat_Series()
    {
        // Если все значения одинаковы, EMA должна равняться этому значению.
        var values = Enumerable.Repeat(50m, 30).ToArray();

        var result = EmaCalculator.Compute(values, period: 10);

        result.Should().BeApproximately(50m, precision: 0.0001m);
    }
}


