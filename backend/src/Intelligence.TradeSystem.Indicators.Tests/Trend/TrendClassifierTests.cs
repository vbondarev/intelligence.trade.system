using FluentAssertions;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Indicators.Trend;

namespace Intelligence.TradeSystem.Indicators.Tests.Trend;

public sealed class TrendClassifierTests
{
    // ── Direction classification ─────────────────────────────────────────────

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
    public void Returns_Sideways_When_Bullish_Alignment_But_Price_Is_Not_Above_Ema200()
    {
        // Строгое условие: price должен быть > EMA200. При price == EMA200 подтверждения нет.
        var (trend, score) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 100m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.49m);
    }

    [Fact]
    public void Returns_Sideways_When_Bearish_Alignment_But_Price_Is_Not_Below_Ema200()
    {
        // Строгое условие: price должен быть < EMA200. При price == EMA200 подтверждения нет.
        var (trend, score) = TrendClassifier.Classify(100m, 150m, 200m, currentPrice: 200m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.49m);
    }

    // ── Strength score: directed trends ─────────────────────────────────────

    [Fact]
    public void StrengthScore_Is_0_8_For_Full_Bullish_Alignment_Without_Volume_Boost()
    {
        // volumeRatio == 1 → boost == 0; направленный тренд получает базовый score = 0.80.
        var (_, score) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1m);

        score.Should().Be(0.8m);
    }

    [Fact]
    public void StrengthScore_Is_0_8_For_Full_Bearish_Alignment_Without_Volume_Boost()
    {
        var (_, score) = TrendClassifier.Classify(100m, 150m, 200m, currentPrice: 90m, volumeRatio: 1m);

        score.Should().Be(0.8m);
    }

    [Fact]
    public void VolumeBoost_Is_Applied_For_Bullish_Trend_When_VolumeRatio_Greater_Than_1()
    {
        // baseScore = 0.80; при volumeRatio = 3 → boost = min((3-1)*0.1, 0.2) = 0.20.
        // Итог: 0.80 + 0.20 = 1.00.
        var (_, scoreBase) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1m);
        var (_, scoreBoosted) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 3m);

        scoreBase.Should().Be(0.8m);
        scoreBoosted.Should().Be(1m);
        scoreBoosted.Should().BeGreaterThan(scoreBase);
    }

    [Fact]
    public void VolumeBoost_Is_Applied_For_Bearish_Trend_When_VolumeRatio_Greater_Than_1()
    {
        var (_, scoreBase) = TrendClassifier.Classify(100m, 150m, 200m, currentPrice: 90m, volumeRatio: 1m);
        var (_, scoreBoosted) = TrendClassifier.Classify(100m, 150m, 200m, currentPrice: 90m, volumeRatio: 3m);

        scoreBase.Should().Be(0.8m);
        scoreBoosted.Should().Be(1m);
        scoreBoosted.Should().BeGreaterThan(scoreBase);
    }

    [Fact]
    public void VolumeBoost_Is_Capped_At_0_2_For_Directed_Trend()
    {
        // volumeRatio = 3 и volumeRatio = 1000 одинаково упираются в MaxVolumeBoost = 0.20.
        var (_, scoreAtCapEdge) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 3m);
        var (_, scoreExtremeVolume) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1000m);

        scoreAtCapEdge.Should().Be(1m);
        scoreExtremeVolume.Should().Be(1m);
    }

    [Fact]
    public void StrengthScore_Does_Not_Exceed_1_With_Extreme_VolumeRatio()
    {
        var (_, score) = TrendClassifier.Classify(200m, 150m, 100m, currentPrice: 210m, volumeRatio: 1000m);

        score.Should().BeLessThanOrEqualTo(1m);
    }

    // ── Strength score: sideways contracts ──────────────────────────────────

    [Fact]
    public void Sideways_StrengthScore_Is_Not_Greater_Than_0_49()
    {
        // Две из трёх bullish-компонент активны, но полного подтверждения тренда нет.
        // Score должен быть ограничен сверху 0.49, чтобы Sideways не выглядел как сильный тренд.
        var (trend, score) = TrendClassifier.Classify(150m, 100m, 130m, currentPrice: 140m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.49m);
    }

    [Fact]
    public void VolumeBoost_Is_Not_Applied_For_Sideways()
    {
        // Для Sideways score зависит только от structural points; повышенный volume не должен усиливать его.
        var (trendBase, scoreBase) = TrendClassifier.Classify(150m, 100m, 130m, currentPrice: 140m, volumeRatio: 1m);
        var (trendHighVolume, scoreHighVolume) = TrendClassifier.Classify(150m, 100m, 130m, currentPrice: 140m, volumeRatio: 3m);

        trendBase.Should().Be(MarketTrend.Sideways);
        trendHighVolume.Should().Be(MarketTrend.Sideways);
        scoreBase.Should().Be(0.49m);
        scoreHighVolume.Should().Be(0.49m);
    }

    [Fact]
    public void Negative_VolumeRatio_Is_Treated_As_Zero()
    {
        var resultWithNegativeVolume = TrendClassifier.Classify(150m, 100m, 130m, currentPrice: 140m, volumeRatio: -5m);
        var resultWithZeroVolume = TrendClassifier.Classify(150m, 100m, 130m, currentPrice: 140m, volumeRatio: 0m);

        resultWithNegativeVolume.Should().Be(resultWithZeroVolume);
    }

    [Fact]
    public void Returns_Expected_Sideways_Score_For_Partial_Bullish_Structure()
    {
        // Только один bullish-сигнал из трёх:
        //   ema20 > ema50  → +0.33
        //   ema50 > ema200 → +0.00 (равны)
        //   price > ema200 → +0.00 (равны)
        // Bearish-сигналов тоже нет → итоговый score = 0.33.
        var (trend, score) = TrendClassifier.Classify(120m, 100m, 100m, currentPrice: 100m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.33m);
    }

    [Fact]
    public void Returns_Expected_Sideways_Score_For_Partial_Bearish_Structure()
    {
        // Только один bearish-сигнал из трёх:
        //   ema20 < ema50  → +0.33
        //   ema50 < ema200 → +0.00 (равны)
        //   price < ema200 → +0.00 (равны)
        // Bullish-сигналов нет → итоговый score = 0.33.
        var (trend, score) = TrendClassifier.Classify(80m, 100m, 100m, currentPrice: 100m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.33m);
    }

    [Fact]
    public void Returns_Expected_Sideways_Score_For_Price_Only_Bias()
    {
        // Только price > EMA200 даёт +0.34. Это полезный regression-тест на вес именно ценового подтверждения.
        var (trend, score) = TrendClassifier.Classify(100m, 100m, 100m, currentPrice: 101m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.34m);
    }

    [Fact]
    public void Returns_Expected_Sideways_Score_For_Bearish_Price_Only_Bias()
    {
        // Зеркальный тест: только price < EMA200 даёт +0.34 для bearish bias.
        // Фиксирует симметрию весового правила для ценового сигнала.
        var (trend, score) = TrendClassifier.Classify(100m, 100m, 100m, currentPrice: 99m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0.34m);
    }

    [Fact]
    public void Returns_Zero_Score_When_All_Inputs_Are_Neutral()
    {
        // Baseline: полностью нейтральный рынок — все EMA равны, цена на EMA200, volume = 1.
        // Ни один из сигналов не активен → StrengthScore = 0.
        // Фиксирует "нулевую точку" classifier'а и защищает от случайного ненулевого bias.
        var (trend, score) = TrendClassifier.Classify(100m, 100m, 100m, currentPrice: 100m, volumeRatio: 1m);

        trend.Should().Be(MarketTrend.Sideways);
        score.Should().Be(0m);
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
        [150m, 100m, 130m, 140m, -5.0m], // Sideways, dirty negative volume
        [100m, 100m, 100m, 100m, 1.0m],  // All equal
    ];
}


