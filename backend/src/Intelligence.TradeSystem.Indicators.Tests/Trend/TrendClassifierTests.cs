using FluentAssertions;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Indicators.Trend;

namespace Intelligence.TradeSystem.Indicators.Tests.Trend;

public sealed class TrendClassifierTests
{
    [Fact]
    public void Returns_Bullish_When_Ema20_Greater_Ema50_Greater_Ema200()
    {
        var (trend, _) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Bullish);
    }

    [Fact]
    public void Returns_Bearish_When_Ema20_Less_Ema50_Less_Ema200()
    {
        var (trend, _) = TrendClassifier.Classify(100m, 150m, 200m, currentPrice: 90m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Bearish);
    }

    [Fact]
    public void Returns_Sideways_When_Emas_Are_Mixed()
    {
        // EMA20 > EMA200 > EMA50 — не полное выравнивание
        var (trend, _) = TrendClassifier.Classify(150m, 100m, 130m, currentPrice: 140m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
    }

    [Fact]
    public void StrengthScore_Is_1_For_Full_Bullish_Alignment_Without_Volume_Boost()
    {
        // volumeRatio == 1 → boost == 0; полное выравнивание → baseScore == 1
        var (_, score) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1m);

        score.Should().Be(1m);
    }

    [Fact]
    public void StrengthScore_Is_1_For_Full_Bearish_Alignment_Without_Volume_Boost()
    {
        var (_, score) = TrendClassifier.Classify(100m, 150m, 200m, currentPrice: 90m, volumeRatio: 1m);

        score.Should().Be(1m);
    }

    [Fact]
    public void StrengthScore_Is_Boosted_When_VolumeRatio_Greater_Than_1()
    {
        // Бычье выравнивание, но базовый score = 1 → boost ограничен Math.Min(1m)
        // Берём боковик с частичным score, чтобы буст был виден
        var (_, scoreBase)    = TrendClassifier.Classify(150m, 100m, 130m, 140m, volumeRatio: 1m);
        var (_, scoreBoosted) = TrendClassifier.Classify(150m, 100m, 130m, 140m, volumeRatio: 3m);

        scoreBoosted.Should().BeGreaterThan(scoreBase);
    }

    [Fact]
    public void StrengthScore_Does_Not_Exceed_1_With_Extreme_VolumeRatio()
    {
        var (_, score) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1000m);

        score.Should().BeLessThanOrEqualTo(1m);
    }

    [Theory]
    [MemberData(nameof(ScoreRangeTestCases))]
    public void StrengthScore_Is_Always_In_Range_0_To_1(
        decimal ema20, decimal ema50, decimal ema200, decimal price, decimal volRatio)
    {
        var (_, score) = TrendClassifier.Classify(ema20, ema50, ema200, price, volRatio);

        score.Should().BeInRange(0m, 1m);
    }

    public static IEnumerable<object[]> ScoreRangeTestCases =>
    [
        [200m, 150m, 100m, 210m, 0.5m],  // Bullish, low volume
        [100m, 150m, 200m,  90m, 2.0m],  // Bearish, high volume
        [150m, 100m, 130m, 140m, 1.0m],  // Sideways
        [100m, 100m, 100m, 100m, 1.0m],  // All equal
    ];
}


