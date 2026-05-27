using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;
using Intelligence.TradeSystem.Indicators.Results;

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

    // ── Boundary ─────────────────────────────────────────────────────────────

    [Fact]
    public void Returns_Unavailable_EmptyInput_When_Array_Is_Empty()
    {
        var result = RsiCalculator.Compute([]);

        result.Value.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.EmptyInput);
    }

    [Fact]
    public void Returns_Unavailable_InsufficientData_When_Data_Is_Insufficient()
    {
        var result = RsiCalculator.Compute([100m, 101m, 102m], period: 14);

        result.Value.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.InsufficientData);
    }

    [Fact]
    public void Returns_Unavailable_InsufficientData_When_Single_Element()
    {
        // Один элемент: closes.Length(1) < period+1(15) → InsufficientData (не EmptyInput).
        var result = RsiCalculator.Compute([100m], period: 14);

        result.Value.Should().BeNull();
        result.IsAvailable.Should().BeFalse();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.InsufficientData);
    }

    // ── Formula cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Returns_Available_100_When_Only_Gains()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 100m + i).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Value.Should().Be(100m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_0_When_Only_Losses()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 300m - i).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Value.Should().BeApproximately(0m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_50_When_All_Prices_Are_Identical()
    {
        var closes = Enumerable.Repeat(100m, 30).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Value.Should().Be(50m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Value_Is_Always_In_Range_0_To_100()
    {
        var closes = new[]
            { 10m, 20m, 5m, 15m, 8m, 25m, 12m, 30m, 3m, 18m, 22m, 7m, 14m, 28m, 11m, 9m };

        var result = RsiCalculator.Compute(closes);

        result.IsAvailable.Should().BeTrue();
        result.Value.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public void Returns_Above_70_For_Strong_Uptrend()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 100m + i * 5m).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.IsAvailable.Should().BeTrue();
        result.Value.Should().BeGreaterThan(70m);
    }

    [Fact]
    public void Returns_Below_30_For_Strong_Downtrend()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 250m - i * 5m).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.IsAvailable.Should().BeTrue();
        result.Value.Should().BeLessThan(30m);
    }

    // ── Invariant: RSI always in [0, 100] when available ─────────────────────

    public static TheoryData<decimal[]> AvailableRsiSeries => new()
    {
        // Строго растущая серия
        Enumerable.Range(0, 30).Select(i => 100m + i * 3m).ToArray(),
        // Строго падающая серия
        Enumerable.Range(0, 30).Select(i => 200m - i * 3m).ToArray(),
        // Плоская серия
        Enumerable.Repeat(100m, 30).ToArray(),
        // Чередование роста и падения
        Enumerable.Range(0, 30).Select(i => i % 2 == 0 ? 100m : 110m).ToArray(),
        // Сильный спайк вверх в конце
        Enumerable.Repeat(100m, 25).Append(10000m).ToArray(),
        // Сильный спайк вниз в конце
        Enumerable.Repeat(100m, 25).Append(0.01m).ToArray(),
        // Минимально необходимая длина: period+1 при period=14
        Enumerable.Range(0, 15).Select(i => 100m + i).ToArray(),
    };

    [Theory]
    [MemberData(nameof(AvailableRsiSeries))]
    public void Rsi_Is_Always_In_Range_0_To_100_When_Available(decimal[] closes)
    {
        var result = RsiCalculator.Compute(closes);

        result.IsAvailable.Should().BeTrue();
        result.Value.Should().BeInRange(0m, 100m,
            because: "RSI is mathematically bounded to [0, 100] by definition");
    }

    // ── Formula regression ───────────────────────────────────────────────────

    [Fact]
    public void Returns_Available_When_Count_Equals_Period_Plus_One()
    {
        // closes=[100, 106, 104], period=2
        // Changes: +6, -2 → avgGain=3, avgLoss=1 → RS=3 → RSI=75
        var result = RsiCalculator.Compute([100m, 106m, 104m], period: 2);

        result.Value.Should().BeApproximately(75m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Rsi_For_Known_Wilder_Series()
    {
        // closes=[10, 13, 11, 15, 12], period=2 → RSI=44
        var result = RsiCalculator.Compute([10m, 13m, 11m, 15m, 12m], period: 2);

        result.Value.Should().BeApproximately(44m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_Rsi_For_Mixed_Deterministic_Series()
    {
        // closes=[10,14,10,14,10,14,10,14], period=2 → RSI=67.1875
        decimal[] closes = [10m, 14m, 10m, 14m, 10m, 14m, 10m, 14m];

        var result = RsiCalculator.Compute(closes, period: 2);

        result.Value.Should().BeApproximately(67.1875m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    // ── Special cases: period = 1 ────────────────────────────────────────────

    [Fact]
    public void Returns_Available_100_For_Period_One_When_Last_Move_Is_Up()
    {
        var result = RsiCalculator.Compute([100m, 110m], period: 1);

        result.Value.Should().Be(100m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_0_For_Period_One_When_Last_Move_Is_Down()
    {
        var result = RsiCalculator.Compute([100m, 90m], period: 1);

        result.Value.Should().BeApproximately(0m, precision: 0.0001m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }

    [Fact]
    public void Returns_Available_50_For_Period_One_When_Last_Move_Is_Flat()
    {
        var result = RsiCalculator.Compute([100m, 100m], period: 1);

        result.Value.Should().Be(50m);
        result.IsAvailable.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Reason.Should().Be(IndicatorValueReason.None);
    }
}
