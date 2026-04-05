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

    [Fact]
    public void Handles_Inverted_High_Low_Data_Gracefully()
    {
        // Плохие данные: high < low (инвертированная свеча).
        // Реализация использует Math.Abs для всех разностей — результат идентичен корректным данным.
        // Тест фиксирует контракт: инверсия H/L не выбрасывает исключение и даёт тот же TR.
        decimal[] highs  = [100m,  90m];   // "high" < "low" — намеренная инверсия
        decimal[] lows   = [ 90m, 110m];
        decimal[] closes = [100m, 105m];

        // TR = max(|90-110|=20, |90-100|=10, |110-100|=10) = 20 — совпадает с H=110, L=90
        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(20m, precision: 0.0001m);
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

    [Fact]
    public void Returns_Seed_Average_When_TrueRange_Count_Equals_Period()
    {
        // Граничный случай: trueRanges.Length == period → Wilder loop выполняется 0 раз.
        // Защита от off-by-one: если loop стартовал бы с (period - 1), лишний шаг дал бы:
        //   ATR = ((20×2) + 30) / 3 = 23.33 — тест поймал бы эту регрессию.
        //
        // TR = [10, 20, 30], period = 3 → Seed = (10+20+30)/3 = 20
        decimal[] highs  = [110m, 110m, 130m, 160m];
        decimal[] lows   = [100m, 100m, 110m, 130m];
        decimal[] closes = [100m, 110m, 130m, 160m];

        var result = AtrCalculator.Compute(highs, lows, closes, period: 3);

        result.Should().BeApproximately(20m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Expected_Atr_For_Long_Deterministic_Series()
    {
        // Regression-тест: 6 свечей, period=3 → TR = [9, 9, 9, 18, 27]
        //
        // Расчёт TR (prevClose всегда совпадает с L следующей свечи → нет гэпов):
        //   C1: max(|109-100|=9,  |109-100|=9,  |100-100|=0) = 9
        //   C2: max(|113-104|=9,  |113-104|=9,  |104-104|=0) = 9
        //   C3: max(|117-108|=9,  |117-108|=9,  |108-108|=0) = 9
        //   C4: max(|130-112|=18, |130-112|=18, |112-112|=0) = 18
        //   C5: max(|147-120|=27, |147-120|=27, |120-120|=0) = 27
        //
        // Seed ATR   = (9+9+9)/3     =  9
        // Wilder i=3 = ((9×2)+18)/3  = 12
        // Wilder i=4 = ((12×2)+27)/3 = 17
        decimal[] highs  = [109m, 109m, 113m, 117m, 130m, 147m];
        decimal[] lows   = [100m, 100m, 104m, 108m, 112m, 120m];
        decimal[] closes = [100m, 104m, 108m, 112m, 120m, 133m];

        var result = AtrCalculator.Compute(highs, lows, closes, period: 3);

        result.Should().BeApproximately(17m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Constant_Atr_When_TrueRange_Is_Constant()
    {
        // 20 идентичных свечей: H=110, L=100, C=100
        // TR = max(|110-100|=10, |110-100|=10, |100-100|=0) = 10 для каждой пары
        //
        // Seed = 10; Wilder: ((10×(p-1))+10)/p = 10p/p = 10 — константа на каждом шаге.
        var highs  = Enumerable.Repeat(110m, 20).ToArray();
        var lows   = Enumerable.Repeat(100m, 20).ToArray();
        var closes = Enumerable.Repeat(100m, 20).ToArray();

        var result = AtrCalculator.Compute(highs, lows, closes, period: 3);

        result.Should().BeApproximately(10m, precision: 0.0001m);
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

    [Fact]
    public void Ignores_Extra_Elements_Beyond_Minimum_Common_Length()
    {
        // lows.Length=3 → count=3 → элементы highs[3]=1000 и closes[3]=2000 не используются.
        // Если бы реализация ошибочно их учла, TR >> 100 и ATR был бы несравнимо больше 17.5.
        //
        // TR[0] = max(|110-100|=10, |110-90|=20, |100-90|=10) = 20
        // TR[1] = max(|120-110|=10, |120-105|=15, |110-105|=5) = 15
        // 2 TR < period=14 → simple average = (20+15)/2 = 17.5
        decimal[] highs  = [100m, 110m, 120m, 1000m];  // 1000 — «яд»: не должен участвовать в расчёте
        decimal[] lows   = [ 90m, 100m, 110m];           // кратчайший массив: длина 3
        decimal[] closes = [ 90m, 105m, 115m, 2000m];   // 2000 — «яд»: не должен участвовать в расчёте

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(17.5m, precision: 0.0001m);
    }
}
