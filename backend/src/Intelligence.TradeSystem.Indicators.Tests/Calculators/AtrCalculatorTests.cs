using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Calculators;

namespace Intelligence.TradeSystem.Indicators.Tests.Calculators;

public sealed class AtrCalculatorTests
{
    [Fact]
    public void Returns_Zero_When_Single_Candle()
    {
        var result = AtrCalculator.Compute([100m], [90m], [95m]);

        result.Should().Be(0m);
    }

    [Fact]
    public void True_Range_Equals_HL_When_No_Gap()
    {
        // H=110, L=90, PrevClose=100 → TR = max(20, |110-100|, |90-100|) = max(20,10,10) = 20
        decimal[] highs  = [100m, 110m];
        decimal[] lows   = [90m,  90m];
        decimal[] closes = [100m, 105m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(20m, precision: 0.0001m);
    }

    [Fact]
    public void True_Range_Uses_PrevClose_When_Gap_Up()
    {
        // H=130, L=120, PrevClose=100 → TR = max(10, |130-100|, |120-100|) = max(10,30,20) = 30
        decimal[] highs  = [100m, 130m];
        decimal[] lows   = [95m,  120m];
        decimal[] closes = [100m, 125m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(30m, precision: 0.0001m);
    }

    [Fact]
    public void True_Range_Uses_PrevClose_When_Gap_Down()
    {
        // H=80, L=70, PrevClose=100 → TR = max(10, |80-100|, |70-100|) = max(10,20,30) = 30
        decimal[] highs  = [100m, 80m];
        decimal[] lows   = [90m,  70m];
        decimal[] closes = [100m, 75m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(30m, precision: 0.0001m);
    }

    [Fact]
    public void Returns_Simple_Average_When_Count_Less_Than_Period()
    {
        // 3 свечи, period=14 → трейнджей 2 штуки → simple average
        // TR[0] = max(10, |110-95|, |100-95|) = max(10,15,5) = 15
        // TR[1] = max(10, |120-105|, |110-105|) = max(10,15,5) = 15
        decimal[] highs  = [100m, 110m, 120m];
        decimal[] lows   = [90m,  100m, 110m];
        decimal[] closes = [95m,  105m, 115m];

        var result = AtrCalculator.Compute(highs, lows, closes);

        result.Should().BeApproximately(15m, precision: 0.0001m);
    }
}

