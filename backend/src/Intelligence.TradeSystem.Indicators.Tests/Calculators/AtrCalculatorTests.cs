using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class AtrCalculatorTests
{
    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Throws_ArgumentNullException_When_Highs_Is_Null()
    {
        var act = () => AtrCalculator.Compute(null!, [90m, 100m], [95m, 105m]);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("highs");
    }

    [Fact]
    public void Throws_ArgumentNullException_When_Lows_Is_Null()
    {
        var act = () => AtrCalculator.Compute([100m, 110m], null!, [95m, 105m]);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("lows");
    }

    [Fact]
    public void Throws_ArgumentNullException_When_Closes_Is_Null()
    {
        var act = () => AtrCalculator.Compute([100m, 110m], [90m, 100m], null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("closes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Throws_ArgumentOutOfRangeException_When_Period_Is_Not_Positive(int period)
    {
        var act = () => AtrCalculator.Compute([100m, 110m], [90m, 100m], [95m, 105m], period);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(period));
    }

    // ── Insufficient data (< 2 candles) ─────────────────────────────────────

    [Fact]
    public void Returns_Zero_When_Arrays_Are_Empty()
    {
        var result = AtrCalculator.Compute([], [], []);

        result.Should().Be(0m);
    }

    [Fact]
    public void Returns_Zero_When_Single_Candle()
    {
        var result = AtrCalculator.Compute([100m], [90m], [95m]);

        result.Should().Be(0m);
    }

    // ── True Range calculation ───────────────────────────────────────────────

    [Fact]
    public void Returns_Zero_For_Flat_Candles()
    {
        // H = L = PrevClose → все три компоненты TR равны 0
        decimal[] highs  = [100m, 100m, 100m, 100m];
        decimal[] lows   = [100m, 100m, 100m, 100m];
        decimal[] closes = [100m, 100m, 100m, 100m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().Be(0m);
    }

    [Fact]
    public void True_Range_Equals_HL_Spread_When_No_Gap()
    {
        // H=110, L=90, PrevClose=100 → TR = max(20, |110-100|=10, |90-100|=10) = 20
        decimal[] highs  = [100m, 110m];
        decimal[] lows   = [ 90m,  90m];
        decimal[] closes = [100m, 105m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(20m, precision: 0.0001m);
    }

    [Fact]
    public void True_Range_Uses_PrevClose_When_Gap_Up()
    {
        // H=130, L=120, PrevClose=100
        // TR = max(|130-120|=10, |130-100|=30, |120-100|=20) = 30
        decimal[] highs  = [100m, 130m];
        decimal[] lows   = [ 95m, 120m];
        decimal[] closes = [100m, 125m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(30m, precision: 0.0001m);
    }

    [Fact]
    public void True_Range_Uses_PrevClose_When_Gap_Down()
    {
        // H=80, L=70, PrevClose=100
        // TR = max(|80-70|=10, |80-100|=20, |70-100|=30) = 30
        decimal[] highs  = [100m,  80m];
        decimal[] lows   = [ 90m,  70m];
        decimal[] closes = [100m,  75m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(30m, precision: 0.0001m);
    }

    // ── Averaging behaviour ──────────────────────────────────────────────────

    [Fact]
    public void Returns_Simple_Average_When_Count_Less_Than_Period()
    {
        // 3 свечи, period=14 → доступно 2 TR-значения → simple average
        // TR[0] = max(|110-100|=10, |110-95|=15, |100-95|=5) = 15
        // TR[1] = max(|120-110|=10, |120-105|=15, |110-105|=5) = 15
        // Average = (15 + 15) / 2 = 15
        decimal[] highs  = [100m, 110m, 120m];
        decimal[] lows   = [ 90m, 100m, 110m];
        decimal[] closes = [ 95m, 105m, 115m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(15m, precision: 0.0001m);
    }

    [Fact]
    public void Applies_Wilder_Smoothing_When_Count_Exceeds_Period()
    {
        // 4 свечи, period=2 → TR = [3, 4, 5]
        //
        // Расчёт TR:
        //   i=1: max(|12-9|=3,  |12-9|=3,   |9-9|=0)   = 3
        //   i=2: max(|15-12|=3, |15-11|=4,  |12-11|=1)  = 4
        //   i=3: max(|18-15|=3, |18-13|=5,  |15-13|=2)  = 5
        //
        // Seed ATR (SMA первых period=2): (3+4)/2 = 3.5
        // Wilder step:  ATR = ((3.5 × 1) + 5) / 2 = 4.25
        decimal[] highs  = [10m, 12m, 15m, 18m];
        decimal[] lows   = [ 8m,  9m, 12m, 15m];
        decimal[] closes = [ 9m, 11m, 13m, 17m];

        var result = AtrCalculator.Compute(highs, lows, closes, period: 2);

        result.Should().BeApproximately(4.25m, precision: 0.0001m);
    }

    [Fact]
    public void Wilder_Smoothing_Gives_More_Weight_To_Recent_Candles()
    {
        // Серия: 14 одинаковых свечей (TR=5) + финальная с TR=50 (спайк).
        // Wilder сглаживает медленнее simple average, но последнее значение всё равно
        // тянет ATR вверх. Итоговое ATR должно быть заметно выше базового уровня 5.
        var count = 16; // 15 TR после 16 свечей
        var highs  = Enumerable.Repeat(105m, count - 1).Append(150m).ToArray();
        var lows   = Enumerable.Repeat( 95m, count - 1).Append(140m).ToArray();
        var closes = Enumerable.Repeat(100m, count).ToArray();

        // TR для первых 14 пар = max(10, 5, 5) = 10 (стабильный базис)
        // TR для последней пары = max(10, 50, 40) = 50 (спайк)

        var result = AtrCalculator.Compute(highs, lows, closes, period: 14);

        result.Should().BeGreaterThan(10m);  // выше базового ATR
        result.Should().BeLessThan(50m);     // но ниже спайка (сглаживание работает)
    }

    // ── Array length mismatch ────────────────────────────────────────────────

    [Fact]
    public void Uses_Minimum_Array_Length_When_Arrays_Have_Different_Lengths()
    {
        // highs.Length=3, lows.Length=2, closes.Length=3 → count=2 → 1 TR значение
        // TR[0]: max(|110-100|=10, |110-90|=20, |100-90|=10) = 20
        // 1 TR < period=14 → simple average → 20
        decimal[] highs  = [100m, 110m, 120m];
        decimal[] lows   = [ 90m, 100m];          // короче остальных
        decimal[] closes = [ 90m, 105m, 115m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(20m, precision: 0.0001m);
    }
}
