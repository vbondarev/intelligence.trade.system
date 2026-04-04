using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

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

    // ── Smoothing formula ────────────────────────────────────────────────────

    [Fact]
    public void Uses_Classic_Smoothing_Formula_For_Known_Series()
    {
        // period=3 → k = 2/(3+1) = 0.5
        //
        // Seed = (10+20+30)/3         = 20
        // i=3: EMA = 40×0.5 + 20×0.5 = 30
        // i=4: EMA = 50×0.5 + 30×0.5 = 40
        //
        // Ловит ошибки:
        //   k = 2/period вместо 2/(period+1)  → k=0.667 → i=4 даст ≈44.4 ≠ 40
        //   loop с period-1                   → лишний шаг → i=4 даст 41.25 ≠ 40
        //   seed из первых 2 значений          → seed=15  → i=4 даст 38.75 ≠ 40
        decimal[] values = [10m, 20m, 30m, 40m, 50m];

        var result = EmaCalculator.Compute(values, period: 3);

        result.Should().BeApproximately(40m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Expected_Ema_For_Long_Deterministic_Series()
    {
        // period=4 → k = 2/(4+1) = 0.4
        //
        // Seed = (10+20+30+40)/4        = 25
        // i=4: EMA = 25×0.4 + 25×0.6   = 10+15 = 25   (seed = last seed element)
        // i=5: EMA = 35×0.4 + 25×0.6   = 14+15 = 29
        // i=6: EMA = 45×0.4 + 29×0.6   = 18+17.4 = 35.4
        decimal[] values = [10m, 20m, 30m, 40m, 25m, 35m, 45m];

        var result = EmaCalculator.Compute(values, period: 4);

        result.Should().BeApproximately(35.4m, precision: 0.0001m);
    }

    [Fact]
    public void Reacts_Faster_Than_Sma_In_Falling_Series()
    {
        // Зеркальный тест к Gives_More_Weight_To_Recent_Values_In_Rising_Series.
        // Серия: 15 значений = 100 (flat), затем резкий обвал до 10.
        // k = 2/(5+1) = 1/3 → EMA = 10×(1/3) + 100×(2/3) = 70
        // SMA последних 5 = (100×4 + 10)/5 = 82
        // EMA(70) < SMA(82) — EMA реагирует быстрее.
        var values = Enumerable.Repeat(100m, 15).Append(10m).ToArray();
        var period = 5;

        var ema = EmaCalculator.Compute(values, period);
        var sma = SmaCalculator.Compute(values, period);

        ema.Should().BeLessThan(sma);
    }
}


