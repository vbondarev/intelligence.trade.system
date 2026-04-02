using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class RsiCalculatorTests
{
    [Fact]
    public void Returns_50_When_Insufficient_Data()
    {
        var closes = new[] { 100m, 101m, 102m }; // < period + 1

        var result = RsiCalculator.Compute(closes);

        result.Should().Be(50m);
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

        result.Should().BeApproximately(0m, precision: 0.0001m);
    }

    [Fact]
    public void Result_Is_Always_In_Range_0_To_100()
    {
        var closes = new[]
            { 10m, 20m, 5m, 15m, 8m, 25m, 12m, 30m, 3m, 18m, 22m, 7m, 14m, 28m, 11m, 9m };

        var result = RsiCalculator.Compute(closes);

        result.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public void Returns_Above_70_For_Strong_Uptrend()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 100m + i * 5m).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().BeGreaterThan(70m);
    }

    [Fact]
    public void Returns_Below_30_For_Strong_Downtrend()
    {
        var closes = Enumerable.Range(0, 30).Select(i => 250m - i * 5m).ToArray();

        var result = RsiCalculator.Compute(closes);

        result.Should().BeLessThan(30m);
    }
}

