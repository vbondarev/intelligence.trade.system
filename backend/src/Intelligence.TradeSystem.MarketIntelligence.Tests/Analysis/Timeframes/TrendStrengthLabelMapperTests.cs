
namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Timeframes;

/// <summary>
/// Unit-тесты для <see cref="TrendStrengthLabelMapper"/>.
/// Покрывают все 7 обязательных кейсов спецификации V1, граничные значения и консистентность.
/// </summary>
public sealed class TrendStrengthLabelMapperTests
{
    // ─── 7 обязательных кейсов из спецификации V1 ───────────────────────────

    [Fact]
    public void Unknown_With_Zero_Score_Returns_Undefined()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Unknown, 0m)
            .Should().Be(TrendStrengthLabel.Undefined,
                because: "Unknown trend → Undefined regardless of score");
    }

    [Fact]
    public void Unknown_With_High_Score_Returns_Undefined()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Unknown, 0.8m)
            .Should().Be(TrendStrengthLabel.Undefined,
                because: "Unknown trend → Undefined even when score = 0.8");
    }

    [Fact]
    public void Unknown_With_Max_Score_Returns_Undefined()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Unknown, 1.0m)
            .Should().Be(TrendStrengthLabel.Undefined,
                because: "Unknown trend → Undefined even when score = 1.0");
    }

    [Fact]
    public void Bullish_With_Score_0_8_Returns_Strong()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bullish, 0.8m)
            .Should().Be(TrendStrengthLabel.Strong,
                because: "score = 0.80 ≥ StrongThreshold → Strong");
    }

    [Fact]
    public void Bearish_With_Score_0_5_Returns_Moderate()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bearish, 0.5m)
            .Should().Be(TrendStrengthLabel.Moderate,
                because: "score = 0.50 ≥ ModerateThreshold && < StrongThreshold → Moderate");
    }

    [Fact]
    public void Sideways_With_Score_0_49_Returns_Weak()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Sideways, 0.49m)
            .Should().Be(TrendStrengthLabel.Weak,
                because: "score = 0.49 < ModerateThreshold → Weak");
    }

    [Fact]
    public void Bullish_With_Score_0_49_Returns_Weak()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bullish, 0.49m)
            .Should().Be(TrendStrengthLabel.Weak,
                because: "score = 0.49 < ModerateThreshold → Weak");
    }

    // ─── Граничные значения ──────────────────────────────────────────────────

    [Fact]
    public void Score_ExactlyAtStrongThreshold_Returns_Strong()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bullish, TrendStrengthLabelMapper.StrongThreshold)
            .Should().Be(TrendStrengthLabel.Strong,
                because: "score == StrongThreshold (0.80) must return Strong (inclusive lower bound)");
    }

    [Fact]
    public void Score_ExactlyAtModerateThreshold_Returns_Moderate()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bearish, TrendStrengthLabelMapper.ModerateThreshold)
            .Should().Be(TrendStrengthLabel.Moderate,
                because: "score == ModerateThreshold (0.50) must return Moderate (inclusive lower bound)");
    }

    [Fact]
    public void Score_JustBelowModerateThreshold_Returns_Weak()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bullish, TrendStrengthLabelMapper.ModerateThreshold - 0.0001m)
            .Should().Be(TrendStrengthLabel.Weak,
                because: "score just below 0.50 → Weak");
    }

    [Fact]
    public void Score_JustBelowStrongThreshold_Returns_Moderate()
    {
        TrendStrengthLabelMapper.Map(MarketTrend.Bullish, TrendStrengthLabelMapper.StrongThreshold - 0.0001m)
            .Should().Be(TrendStrengthLabel.Moderate,
                because: "score just below 0.80 → Moderate");
    }

    [Fact]
    public void Sideways_With_High_Score_Returns_Strong_Not_Undefined()
    {
        // Sideways это определённый тренд — label считается по score, не Undefined
        TrendStrengthLabelMapper.Map(MarketTrend.Sideways, 0.8m)
            .Should().Be(TrendStrengthLabel.Strong,
                because: "Sideways is a defined trend; score ≥ 0.80 → Strong (not Undefined)");
    }

    // ─── Консистентность: тrendCode == 0 (Unknown) → не Strong/Moderate/Weak ─

    public static IEnumerable<object[]> AllScores =>
    [
        [0.0],
        [0.3],
        [0.5],
        [0.8],
        [1.0],
    ];

    [Theory]
    [MemberData(nameof(AllScores))]
    public void TrendCode_0_Unknown_Never_Returns_Strong_Moderate_Or_Weak(double scoreDouble)
    {
        // MarketTrend.Unknown == (int)0 == trendCode 0
        var score = (decimal)scoreDouble;
        var label = TrendStrengthLabelMapper.Map(MarketTrend.Unknown, score);

        label.Should().NotBe(TrendStrengthLabel.Strong);
        label.Should().NotBe(TrendStrengthLabel.Moderate);
        label.Should().NotBe(TrendStrengthLabel.Weak,
            because: $"trendCode=0 (Unknown) with score={score} must yield Undefined, not a strength label");
    }

    // ─── Консистентность: Sideways никогда не возвращает Undefined ──────────

    [Theory]
    [MemberData(nameof(AllScores))]
    public void Sideways_Never_Returns_Undefined(double scoreDouble)
    {
        var score = (decimal)scoreDouble;
        TrendStrengthLabelMapper.Map(MarketTrend.Sideways, score)
            .Should().NotBe(TrendStrengthLabel.Undefined,
                because: $"Sideways is a defined trend; score={score} must not yield Undefined");
    }

    // ─── Консистентность: trend != Unknown никогда не возвращает Undefined ──

    [Theory]
    [InlineData(MarketTrend.Bullish)]
    [InlineData(MarketTrend.Bearish)]
    [InlineData(MarketTrend.Sideways)]
    public void Defined_Trend_Never_Returns_Undefined(MarketTrend trend)
    {
        foreach (var score in new[] { 0m, 0.1m, 0.49m, 0.5m, 0.79m, 0.8m, 1.0m })
        {
            TrendStrengthLabelMapper.Map(trend, score)
                .Should().NotBe(TrendStrengthLabel.Undefined,
                    because: $"{trend} with score={score} is a defined trend and must not yield Undefined");
        }
    }

    // ─── Консистентность: label == Undefined ←→ trend == Unknown ────────────

    [Theory]
    [InlineData(MarketTrend.Unknown, 0.0, true)]
    [InlineData(MarketTrend.Unknown, 0.9, true)]
    [InlineData(MarketTrend.Bullish, 0.9, false)]
    [InlineData(MarketTrend.Bearish, 0.3, false)]
    [InlineData(MarketTrend.Sideways, 0.5, false)]
    public void Undefined_Label_IFF_Trend_Is_Unknown(MarketTrend trend, double scoreDouble, bool expectUndefined)
    {
        var score = (decimal)scoreDouble;
        var label = TrendStrengthLabelMapper.Map(trend, score);
        var isUndefined = label == TrendStrengthLabel.Undefined;

        isUndefined.Should().Be(expectUndefined,
            because: $"trend={trend}, score={score}: Undefined ←→ Unknown");
    }
}
