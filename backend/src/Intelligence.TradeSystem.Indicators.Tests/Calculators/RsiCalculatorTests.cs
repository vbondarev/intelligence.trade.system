using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class RsiCalculatorTests
{
    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Throws_ArgumentNullException_When_Closes_Is_Null()
    {
        var act = () => RsiCalculator.Compute(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("closes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-14)]
    public void Throws_ArgumentOutOfRangeException_When_Period_Is_Not_Positive(int period)
    {
        var act = () => RsiCalculator.Compute([100m, 101m, 102m], period);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(period));
    }

    // ── Boundary & fallback ──────────────────────────────────────────────────

    [Fact]
    public void Returns_Null_When_Insufficient_Data()
    {
        var closes = new[] { 100m, 101m, 102m }; // < period + 1

        var result = RsiCalculator.Compute(closes);

        result.Should().BeNull();
    }

    [Fact]
    public void Returns_100_When_Only_Gains()
    {
        // Только рост → avgLoss == 0 → RSI = 100
        var closes = Enumerable.Range(0, 30).Select(i => 100m + i).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().Be(100m);
    }

    [Fact]
    public void Returns_0_When_Only_Losses()
    {
        // Только падение → avgGain == 0 → RS = 0 → RSI = 0
        var closes = Enumerable.Range(0, 30).Select(i => 300m - i).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().HaveValue();
        result!.Value.Should().BeApproximately(0m, precision: 0.0001m);
    }

    [Fact]
    public void Result_Is_Always_In_Range_0_To_100()
    {
        var closes = new[]
            { 10m, 20m, 5m, 15m, 8m, 25m, 12m, 30m, 3m, 18m, 22m, 7m, 14m, 28m, 11m, 9m };

        var result = RsiCalculator.Compute(closes);

        result.Should().HaveValue();
        result!.Value.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public void Returns_Above_70_For_Strong_Uptrend()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 100m + i * 5m).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().HaveValue();
        result!.Value.Should().BeGreaterThan(70m);
    }

    [Fact]
    public void Returns_Below_30_For_Strong_Downtrend()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 250m - i * 5m).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().HaveValue();
        result!.Value.Should().BeLessThan(30m);
    }

    [Fact]
    public void Returns_50_When_All_Prices_Are_Identical()
    {
        // Полностью плоский рынок: avgGain == 0 и avgLoss == 0 → нейтральный RSI = 50
        var closes = Enumerable.Repeat(100m, 30).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().Be(50m);
    }

    // ── Formula regression ───────────────────────────────────────────────────

    [Fact]
    public void Returns_Expected_Rsi_When_Count_Equals_Period_Plus_One()
    {
        // Граничный случай: gains.Length == period → Wilder loop 0 итераций (только seed).
        // Ловит off-by-one ошибку, если loop стартует на шаг раньше.
        //
        // closes=[100, 106, 104], period=2
        // Changes: +6, -2  → gains=[6,0], losses=[0,2]
        // Seed: avgGain=(6+0)/2=3, avgLoss=(0+2)/2=1
        // Loop: i от 2 до 1 → 0 итераций
        // RS=3 → RSI = 100 - 100/4 = 75
        var result = RsiCalculator.Compute([100m, 106m, 104m], period: 2);

        result.Should().HaveValue();
        result!.Value.Should().BeApproximately(75m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Expected_Rsi_For_Known_Wilder_Series()
    {
        // closes=[10, 13, 11, 15, 12], period=2
        // Changes: +3, -2, +4, -3  → gains=[3,0,4,0], losses=[0,2,0,3]
        //
        // Seed: avgGain=(3+0)/2=1.5, avgLoss=(0+2)/2=1.0
        // i=2: avgGain=(1.5+4)/2=2.75,  avgLoss=(1.0+0)/2=0.5
        // i=3: avgGain=(2.75+0)/2=1.375, avgLoss=(0.5+3)/2=1.75
        //
        // RS = 1.375/1.75 = 11/14
        // RSI = 100 − 100/(1 + 11/14) = 100 − 100×(14/25) = 100 − 56 = 44
        //
        // Ловит ошибки: неправильный seed, сдвиг цикла, деление gains/losses, формулу RS.
        var result = RsiCalculator.Compute([10m, 13m, 11m, 15m, 12m], period: 2);

        result.Should().HaveValue();
        result!.Value.Should().BeApproximately(44m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Expected_Rsi_For_Mixed_Deterministic_Series()
    {
        // Серия с чередующимися +4/-4, period=2 (6 Wilder-шагов после seed).
        // closes=[10,14,10,14,10,14,10,14]
        // gains=[4,0,4,0,4,0,4], losses=[0,4,0,4,0,4,0]
        //
        // Seed: avgGain=2, avgLoss=2
        // i=2: avgGain=3,      avgLoss=1
        // i=3: avgGain=1.5,    avgLoss=2.5
        // i=4: avgGain=2.75,   avgLoss=1.25
        // i=5: avgGain=1.375,  avgLoss=2.625
        // i=6: avgGain=2.6875, avgLoss=1.3125
        //
        // RS = (43/16)/(21/16) = 43/21
        // RSI = 100 − 100×(21/64) = 100 − 32.8125 = 67.1875
        decimal[] closes = [10m, 14m, 10m, 14m, 10m, 14m, 10m, 14m];

        var result = RsiCalculator.Compute(closes, period: 2);

        result.Should().HaveValue();
        result!.Value.Should().BeApproximately(67.1875m, precision: 0.0001m);
    }

    // ── Special cases: period = 1 ────────────────────────────────────────────

    [Fact]
    public void Returns_100_For_Period_One_When_Last_Move_Is_Up()
    {
        // period=1 → k=1 → каждый Wilder-шаг заменяет avgGain/avgLoss текущим значением.
        // Последнее изменение вверх (+10): avgGain=10, avgLoss=0 → RSI=100.
        var result = RsiCalculator.Compute([100m, 110m], period: 1);

        result.Should().Be(100m);
    }

    [Fact]
    public void Returns_0_For_Period_One_When_Last_Move_Is_Down()
    {
        // Последнее изменение вниз (-10): avgGain=0, avgLoss=10
        // RS=0 → RSI = 100 - 100/(1+0) = 0.
        var result = RsiCalculator.Compute([100m, 90m], period: 1);

        result.Should().HaveValue();
        result!.Value.Should().BeApproximately(0m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_50_For_Period_One_When_Last_Move_Is_Flat()
    {
        // Последнее изменение = 0: avgGain=0, avgLoss=0 → нейтральный RSI=50.
        var result = RsiCalculator.Compute([100m, 100m], period: 1);

        result.Should().Be(50m);
    }
}
