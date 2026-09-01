namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis;

/// <summary>
/// Unit-тесты для <see cref="MarketRegimePolicy"/>.
/// Сценарии перенесены из удалённого <c>Intelligence.TradeSystem.Analytics.Tests.MarketRegimeClassifierTests</c>,
/// поскольку <see cref="MarketRegimePolicy"/> — единственный канонический источник классификации режима.
/// </summary>
public sealed class MarketRegimePolicyTests
{
    [Fact]
    public void Throws_When_H1_Is_Null()
    {
        var action = () => MarketRegimePolicy.Classify(null!, CreateTimeframe("4h"));

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("h1");
    }

    [Fact]
    public void Throws_When_H4_Is_Null()
    {
        var action = () => MarketRegimePolicy.Classify(CreateTimeframe("1h"), null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("h4");
    }

    [Fact]
    public void Returns_Trending_When_Directions_Are_Aligned_And_Average_Strength_Equals_Threshold()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.70m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.50m };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Trending);
    }

    [Fact]
    public void Returns_Trending_For_Aligned_Bearish_Timeframes_With_Sufficient_Strength()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.65m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.80m };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Trending);
    }

    [Fact]
    public void Does_Not_Return_Trending_When_Average_Strength_Is_Below_Threshold()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.59m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.59m };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().NotBe(MarketRegimes.Trending);
        result.Should().Be(MarketRegimes.Neutral);
    }

    [Fact]
    public void Returns_Volatile_When_Timeframes_Have_Conflicting_Directions()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.90m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.90m };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Volatile);
    }

    [Fact]
    public void Returns_Volatile_When_VolumeRatio_Exceeds_Threshold()
    {
        var h1 = CreateTimeframe("1h") with { VolumeRatio = 2.01m };
        var h4 = CreateTimeframe("4h");

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Volatile);
    }

    [Fact]
    public void Does_Not_Return_Volatile_When_VolumeRatio_Equals_Threshold_And_No_Other_Conditions_Apply()
    {
        var h1 = CreateTimeframe("1h") with { VolumeRatio = 2.0m };
        var h4 = CreateTimeframe("4h") with { VolumeRatio = 2.0m };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Neutral);
    }

    [Fact]
    public void Returns_MeanReversion_When_Both_Timeframes_Are_Sideways()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Sideways };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Sideways };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.MeanReversion);
    }

    [Fact]
    public void Returns_MeanReversion_When_Any_Timeframe_Has_Rsi_Extreme()
    {
        var h1 = CreateTimeframe("1h") with { RsiOverbought = true };
        var h4 = CreateTimeframe("4h");

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.MeanReversion);
    }

    [Fact]
    public void Returns_Neutral_When_No_Regime_Conditions_Are_Met()
    {
        var result = MarketRegimePolicy.Classify(CreateTimeframe("1h"), CreateTimeframe("4h"));

        result.Should().Be(MarketRegimes.Neutral);
    }

    [Fact]
    public void Prefers_Trending_Over_Volatile_And_MeanReversion_When_Multiple_Regime_Conditions_Are_True()
    {
        var h1 = CreateTimeframe("1h") with
        {
            Trend = MarketTrend.Bullish,
            TrendStrengthScore = 0.70m,
            VolumeRatio = 3.00m,
            RsiOverbought = true,
        };
        var h4 = CreateTimeframe("4h") with
        {
            Trend = MarketTrend.Bullish,
            TrendStrengthScore = 0.70m,
            RsiOversold = true,
        };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Trending);
    }

    [Fact]
    public void Prefers_Volatile_Over_MeanReversion_When_Both_Regime_Conditions_Are_True()
    {
        var h1 = CreateTimeframe("1h") with
        {
            Trend = MarketTrend.Bullish,
            RsiOverbought = true,
        };
        var h4 = CreateTimeframe("4h") with
        {
            Trend = MarketTrend.Bearish,
            RsiOversold = true,
        };

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Volatile);
    }

    [Fact]
    public void Returns_Deterministic_Result_For_Same_Input()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.75m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.85m };

        var first = MarketRegimePolicy.Classify(h1, h4);
        var second = MarketRegimePolicy.Classify(h1, h4);

        second.Should().Be(first);
    }

    [Fact]
    public void Returns_Neutral_Not_MeanReversion_When_RsiOverbought_But_Rsi14IsReliable_False()
    {
        // RsiOverbought=true, но Rsi14IsReliable=false → RSI-сигнал не должен активировать MeanReversion
        var h1 = CreateTimeframe("1h") with { RsiOverbought = true, Rsi14IsReliable = false };
        var h4 = CreateTimeframe("4h");

        var result = MarketRegimePolicy.Classify(h1, h4);

        result.Should().Be(MarketRegimes.Neutral,
            because: "RsiOverbought без Rsi14IsReliable не должен активировать MeanReversion");
    }

    private static TimeframeAnalysisSnapshot CreateTimeframe(string timeframe) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.Zero),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.Zero),
                Open = 64800m,
                High = 65100m,
                Low = 64750m,
                Close = 65000m,
                Volume = 1200m,
                Turnover = 78000000m,
            },
            Ema20 = 64900m,
            Ema50 = 64850m,
            Ema200 = 64000m,
            Rsi14 = 55m,
            Rsi14IsReliable = true,
            Atr14 = 180m,
            VolumeSma20 = 1000m,
            VolumeRatio = 1.10m,
            TrendStrengthScore = 0.40m,
            Trend = MarketTrend.Unknown,
            Support1 = 64600m,
            Support2 = 64250m,
            Resistance1 = 65200m,
            Resistance2 = 65650m,
            IsAboveEma20 = true,
            IsAboveEma50 = true,
            IsAboveEma200 = true,
            CandleRangePct = 0.5385m,
            DistanceToSupport1Pct = 0.6154m,
            DistanceToResistance1Pct = 0.3077m,
        };
}
