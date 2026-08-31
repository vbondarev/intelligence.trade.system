using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Mapper-level integration tests for <c>LlmPayloadMapperExtensions.ToLlmPayload</c>.
///
/// These tests verify:
/// 1. Higher-TF opposite levels are correctly propagated to lower-TF summary (cross-TF wiring).
/// 2. EntryQuality.Good is impossible in weak/conflicted conditions through the full pipeline.
/// 3. EntryQuality.Good is still reachable in clean setups through the full pipeline.
/// 4. JSON structure is not changed: only entryQuality values change as expected.
/// 5. riskFlags are consistent with entryQuality (no contradictions).
///
/// Tests go through <c>ToLlmPayload</c> the same path as the real API endpoint.
/// </summary>
public sealed class LlmPayloadMapperExtensionsTests
{
    // --- Shared health instances ---------------------------------------------

    private static readonly LlmSnapshotHealthPayload _freshHealth = new()
    {
        IsFresh = true,
        IsPartial = false,
        Warnings = [],
    };

    private static readonly LlmSnapshotHealthPayload _staleHealth = new()
    {
        IsFresh = false,
        IsPartial = false,
        Warnings = ["SnapshotStale"],
    };

    // ===========================================================================
    // Cross-TF level propagation: M15 considers H1/H4 opposite levels
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4HasVeryCloseResistance_M15EntryQualityIsPoor()
    {
        // Arrange: M15 bullish, good conditions, no local resistance.
        // H4 has strong resistance very close (0.05%) > should force M15 to Poor.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToResistance: null,       // no local m15 resistance
                resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h",
                distToResistance: 0.05m,      // very close h4 resistance
                resistanceStrength: 0.85m),   // strong
            regime: MarketRegimes.Trending);

        // Act
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        // Assert
        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "H4 strong resistance at 0.05% < 0.15% threshold must force M15 entryQuality to Poor");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeResistance",
            because: "NearHigherTimeframeResistance flag must be set when H4 resistance is very close");
    }

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4HasNearResistance_M15EntryQualityIsNotGood()
    {
        // H4 resistance at 0.20% (< 0.30%) > Good forbidden for M15.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToResistance: null,
                resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h",
                distToResistance: 0.20m,
                resistanceStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().NotBe("Good",
            because: "H4 resistance at 0.20% < 0.30% threshold must forbid Good for M15");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4HasFarResistance_M15EntryQualityCanBeGood()
    {
        // H4 resistance at 0.50% (>= 0.30%) > no constraint from higher TF.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToResistance: null,
                resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h",
                distToResistance: 0.50m,
                resistanceStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Good",
            because: "H4 resistance at 0.50% >= 0.30% threshold does not restrict M15 entryQuality");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void ToLlmPayload_H1Bullish_WhenD1HasNearResistance_H1EntryQualityIsNotGood()
    {
        // D1 resistance at 0.20% for H1 > Good forbidden.
        var snapshot = MakeSnapshot(
            h1: MakeBullishTf("1h",
                distToResistance: null,
                resistanceStrength: null,
                volumeRatio: 1.2m),
            d1: MakeBullishTf("1d",
                distToResistance: 0.20m,
                resistanceStrength: 0.80m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.H1.Summary.EntryQuality.Should().NotBe("Good",
            because: "D1 resistance at 0.20% acts as higher-TF obstacle for H1");
    }

    [Fact]
    public void ToLlmPayload_M15Bearish_WhenH4HasVeryCloseSupport_M15EntryQualityIsPoor()
    {
        // M15 bearish, H4 has strong support very close (0.05%) > Poor.
        var snapshot = MakeSnapshot(
            m15: MakeBearishTf("15m",
                distToSupport: null,
                supportStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBearishTf("4h",
                distToSupport: 0.05m,
                supportStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "H4 strong support at 0.05% < 0.15% must force M15 bearish entryQuality to Poor");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeSupport");
    }

    [Fact]
    public void ToLlmPayload_HigherTfLevel_OnWrongSideOfPrice_IsIgnored()
    {
        // If higher TF has support/resistance with null distance (level absent) > no constraint.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToResistance: null,
                resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h",
                // Simulate resistance below price: null (behind the trade)
                distToResistance: null,
                resistanceStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Good",
            because: "H4 resistance with null distance (wrong side) must not constrain M15 entryQuality");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    // ===========================================================================
    // BTCUSDT-like regression scenarios via full pipeline
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_BtcUsdtLike_M15Bullish_LowVolume_BelowEmas_NeutralRegime_NearH4Resistance_IsPoor()
    {
        // BTCUSDT m15 scenario:
        // - Bullish bias but below both EMAs
        // - Low volume (0.1971)
        // - Stale snapshot
        // - Neutral market regime
        // - H4 has strong resistance at 0.05% above price
        var m15 = new TimeframeAnalysisSnapshot
        {
            Timeframe = "15m",
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 77_400m, High = 77_500m, Low = 77_350m, Close = 77_437m,
                Volume = 197m, Turnover = 15_260_000m,
            },
            Ema20 = 77_600m,    // above close > isAboveEma20 = false
            Ema50 = 77_550m,    // above close > isAboveEma50 = false
            Ema200 = 75_000m,
            Rsi14 = 45m,
            Rsi14IsReliable = true,
            Atr14 = 200m,
            VolumeSma20 = 1000m,
            VolumeRatio = 0.1971m,             // very low volume
            TrendStrengthScore = 0.6m,
            Trend = MarketTrend.Bullish,
            Support1 = 77_000m,
            Support1Strength = 0.50m,           // Moderate support
            DistanceToSupport1Pct = 0.56m,
            Resistance1 = null,                 // no m15 resistance
            Resistance1Strength = null,
            DistanceToResistance1Pct = null,
            IsAboveEma20 = false,               // EMA conflict
            IsAboveEma50 = false,               // EMA conflict
            IsAboveEma200 = true,
            EmaBullishAlignment = true,
            EmaBearishAlignment = false,
            RsiOverbought = false, RsiOversold = false,
            EmaIsReliable = true, EmaHasFallback = false,
            AtrIsReliable = true, AtrIsFallback = false,
            VolumeRatioIsReliable = true, VolumeRatioIsFallback = false,
            CandleRangePct = 0.19m,
        };

        // H4 has strong resistance at 0.05% above m15's current price
        var h4 = MakeBullishTf("4h",
            distToResistance: 0.05m,
            resistanceStrength: 0.80m,
            volumeRatio: 1.0m);

        var snapshot = MakeSnapshot(m15: m15, h4: h4, regime: MarketRegimes.Neutral);
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _staleHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "BTCUSDT m15: very low volume + EMA conflict + stale snapshot + " +
                     "neutral regime + very close H4 resistance > Poor");
        // Verify raw fields are not altered
        payload.M15.VolumeRatio.Should().Be(0.1971m);
        payload.M15.IsAboveEma20.Should().BeFalse();
        payload.M15.IsAboveEma50.Should().BeFalse();
    }

    [Fact]
    public void ToLlmPayload_BtcUsdtLike_H4Bearish_VeryLowVolume_PriceAboveBothEmas_NeutralRegime_IsNotGood()
    {
        // BTCUSDT h4 scenario:
        // - Bearish bias but price above EMAs (EMA conflict)
        // - Very low volume (0.0184)
        // - Neutral regime
        var h4 = new TimeframeAnalysisSnapshot
        {
            Timeframe = "4h",
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 102_000m, High = 102_100m, Low = 101_800m, Close = 101_900m,
                Volume = 18m, Turnover = 1_834_000m,
            },
            Ema20 = 101_500m,   // below close > isAboveEma20 = true > EMA conflict for bearish
            Ema50 = 101_400m,   // below close > isAboveEma50 = true > EMA conflict for bearish
            Ema200 = 103_000m,
            Rsi14 = 52m,
            Rsi14IsReliable = true,
            Atr14 = 500m,
            VolumeSma20 = 1000m,
            VolumeRatio = 0.0184m,              // extremely low volume
            TrendStrengthScore = 0.7m,
            Trend = MarketTrend.Bearish,
            Resistance1 = 102_000m,
            Resistance1Strength = 0.80m,        // Strong resistance
            DistanceToResistance1Pct = 0.3m,
            Support1 = 100_000m,
            Support1Strength = 0.60m,
            DistanceToSupport1Pct = 1.8m,
            IsAboveEma20 = true,                // EMA conflict for bearish
            IsAboveEma50 = true,                // EMA conflict for bearish
            IsAboveEma200 = false,
            EmaBullishAlignment = false,
            EmaBearishAlignment = true,
            RsiOverbought = false, RsiOversold = false,
            EmaIsReliable = true, EmaHasFallback = false,
            AtrIsReliable = true, AtrIsFallback = false,
            VolumeRatioIsReliable = true, VolumeRatioIsFallback = false,
            CandleRangePct = 0.29m,
        };

        var snapshot = MakeSnapshot(h4: h4, regime: MarketRegimes.Neutral);
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.H4.Summary.EntryQuality.Should().NotBe("Good",
            because: "H4 bearish: very low volume + price above both EMAs + neutral regime > never Good");
        payload.H4.Summary.EntryQuality.Should().BeOneOf("Poor", "Fair");
        // Verify raw data unchanged
        payload.H4.VolumeRatio.Should().Be(0.0184m);
        payload.H4.IsAboveEma20.Should().BeTrue();
        payload.H4.IsAboveEma50.Should().BeTrue();
    }

    // ===========================================================================
    // Clean setups � Good must still be reachable
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_CleanBullishSetup_ReturnsGoodForM15()
    {
        // Clean bullish:
        // - Price above EMA20/EMA50, confirmed trend
        // - Strong support nearby
        // - No resistance on current TF or higher TFs
        // - High volume, fresh snapshot, Trending regime
        var m15 = MakeBullishTf("15m",
            distToSupport: 0.5m,
            supportStrength: 0.85m,     // Strong
            distToResistance: null,
            resistanceStrength: null,
            volumeRatio: 1.2m);

        var snapshot = MakeSnapshot(
            m15: m15,
            h1: MakeBullishTf("1h", distToResistance: null, resistanceStrength: null),
            h4: MakeBullishTf("4h", distToResistance: null, resistanceStrength: null),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Good",
            because: "clean bullish setup: confirmed + strong support + high volume + " +
                     "above both EMAs + fresh + Trending + no resistance obstacle > Good");
        payload.M15.Summary.IsTrendConfirmed.Should().BeTrue();
        payload.M15.Summary.RiskFlags.Should().NotContain("LowVolume");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearResistance");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
        payload.M15.Summary.RiskFlags.Should().NotContain("WeakEntryLevel");
    }

    [Fact]
    public void ToLlmPayload_CleanBearishSetup_ReturnsGoodForH4()
    {
        // Clean bearish:
        // - Price below EMA20/EMA50, confirmed trend
        // - Strong resistance nearby
        // - No support on current TF or higher TFs (D1)
        // - High volume, fresh snapshot, Trending regime
        var h4 = MakeBearishTf("4h",
            distToResistance: 0.4m,
            resistanceStrength: 0.85m,   // Strong
            distToSupport: null,
            supportStrength: null,
            volumeRatio: 1.2m);

        var snapshot = MakeSnapshot(
            h4: h4,
            d1: MakeBearishTf("1d", distToSupport: null, supportStrength: null),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.H4.Summary.EntryQuality.Should().Be("Good",
            because: "clean bearish setup: confirmed + strong resistance + high volume + " +
                     "below both EMAs + fresh + Trending + no support obstacle > Good");
        payload.H4.Summary.IsTrendConfirmed.Should().BeTrue();
        payload.H4.Summary.RiskFlags.Should().NotContain("LowVolume");
        payload.H4.Summary.RiskFlags.Should().NotContain("NearSupport");
        payload.H4.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeSupport");
        payload.H4.Summary.RiskFlags.Should().NotContain("WeakEntryLevel");
    }

    // ===========================================================================
    // ResolveHigherTfOppositeLevel � distance boundary conditions
    // dist == 0 is valid (obstacle exactly at price); dist < 0 is wrong-side (ignored)
    // TODO: mapper-level integration coverage for TrendConfirmedButEntryFiltered with dist==0
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4ResistanceDistanceIsZero_ForcesEntryQualityToPoor()
    {
        // H4 resistance exactly at price (distance=0) � the nearest possible obstacle.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m", distToResistance: null, resistanceStrength: null, volumeRatio: 1.2m),
            h4: MakeBullishTf("4h", distToResistance: 0m, resistanceStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "H4 resistance at distance=0 is directly at price � maximum obstacle, must force Poor");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeResistance",
            because: "distance=0 meets the NearHigherTimeframeResistance threshold");
    }

    [Fact]
    public void ToLlmPayload_M15Bearish_WhenH4SupportDistanceIsZero_ForcesEntryQualityToPoor()
    {
        // H4 support exactly at price (distance=0) � the nearest possible obstacle for bearish.
        var snapshot = MakeSnapshot(
            m15: MakeBearishTf("15m", distToSupport: null, supportStrength: null, volumeRatio: 1.2m),
            h4: MakeBearishTf("4h", distToSupport: 0m, supportStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "H4 support at distance=0 is directly at price � maximum obstacle, must force Poor");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeSupport",
            because: "distance=0 meets the NearHigherTimeframeSupport threshold");
    }

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4ResistanceDistanceIsNegative_IsIgnored()
    {
        // H4 resistance with negative distance is behind the trade (wrong side) > must be ignored.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m", distToResistance: null, resistanceStrength: null, volumeRatio: 1.2m),
            h4: MakeBullishTf("4h", distToResistance: -0.1m, resistanceStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Good",
            because: "H4 resistance with negative distance is on the wrong side of price and must not constrain M15");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void ToLlmPayload_M15Bearish_WhenH4SupportDistanceIsNegative_IsIgnored()
    {
        // H4 support with negative distance is behind the trade (wrong side) > must be ignored.
        var snapshot = MakeSnapshot(
            m15: MakeBearishTf("15m", distToSupport: null, supportStrength: null, volumeRatio: 1.2m),
            h4: MakeBearishTf("4h", distToSupport: -0.1m, supportStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Good",
            because: "H4 support with negative distance is on the wrong side of price and must not constrain M15");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeSupport");
    }

    [Fact]
    public void ToLlmPayload_M15Bullish_MultipleHigherTfCandidates_ZeroAndPositive_SelectsZeroAsNearest()
    {
        // H1: distance=0 (at price), H4: distance=0.25 � zero must win as the nearest obstacle.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m", distToResistance: null, resistanceStrength: null, volumeRatio: 1.2m),
            h1: MakeBullishTf("1h", distToResistance: 0m, resistanceStrength: 0.80m),
            h4: MakeBullishTf("4h", distToResistance: 0.25m, resistanceStrength: 0.80m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "H1 resistance at distance=0 is nearer than H4 at 0.25%; zero wins as nearest obstacle > Poor");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void ToLlmPayload_M15Bullish_MultipleHigherTfCandidates_NegativeAndPositive_SelectsPositiveCandidate()
    {
        // H1: distance=-0.1 (wrong-side, ignored), H4: distance=0.25 � only positive is valid.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m", distToResistance: null, resistanceStrength: null, volumeRatio: 1.2m),
            h1: MakeBullishTf("1h", distToResistance: -0.1m, resistanceStrength: 0.80m),
            h4: MakeBullishTf("4h", distToResistance: 0.25m, resistanceStrength: 0.80m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().NotBe("Good",
            because: "H1 negative distance ignored; H4 resistance at 0.25% < 0.30% threshold > Good forbidden");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeResistance",
            because: "H4 resistance at 0.25% is the selected obstacle and meets the near-resistance threshold");
    }

    // ===========================================================================
    // Pipeline integrity � raw market data must not be altered
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_RawMarketDataFields_AreNotAlteredByEntryQualityLogic()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish);
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        // Raw indicator fields must pass through unmodified
        payload.M15.VolumeRatio.Should().Be(snapshot.M15.VolumeRatio);
        payload.M15.Rsi14.Should().Be(snapshot.M15.Rsi14);
        payload.M15.Ema20.Should().Be(snapshot.M15.Ema20);
        payload.M15.Ema50.Should().Be(snapshot.M15.Ema50);
        payload.M15.Ema200.Should().Be(snapshot.M15.Ema200);
        payload.M15.IsAboveEma20.Should().Be(snapshot.M15.IsAboveEma20);
        payload.M15.IsAboveEma50.Should().Be(snapshot.M15.IsAboveEma50);
        payload.M15.IsAboveEma200.Should().Be(snapshot.M15.IsAboveEma200);
        payload.M15.Support1.Should().Be(snapshot.M15.Support1);
        payload.M15.Resistance1.Should().Be(snapshot.M15.Resistance1);
        payload.M15.DistanceToSupport1Pct.Should().Be(snapshot.M15.DistanceToSupport1Pct);
        payload.M15.DistanceToResistance1Pct.Should().Be(snapshot.M15.DistanceToResistance1Pct);

        // Schema version and structure fields intact
        payload.SchemaVersion.Should().Be("1.0");
        payload.Symbol.Should().Be(snapshot.Symbol);
        payload.Exchange.Should().Be(snapshot.Exchange);
        payload.Sentiment.MarketRegime.Should().Be(snapshot.Sentiment.MarketRegime);
    }

    [Fact]
    public void ToLlmPayload_JsonStructure_AllTimeframesPresent()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Should().NotBeNull();
        payload.H1.Should().NotBeNull();
        payload.H4.Should().NotBeNull();
        payload.D1.Should().NotBeNull();
        payload.M15.Timeframe.Should().Be("15m");
        payload.H1.Timeframe.Should().Be("1h");
        payload.H4.Timeframe.Should().Be("4h");
        payload.D1.Timeframe.Should().Be("1d");

        // Summary fields present in each timeframe
        foreach (var tf in new[] { payload.M15, payload.H1, payload.H4, payload.D1 })
        {
            tf.Summary.Should().NotBeNull();
            tf.Summary.EntryQuality.Should().BeOneOf("Good", "Fair", "Poor");
            tf.Summary.Bias.Should().BeOneOf("Bullish", "Bearish", "Neutral");
            tf.Summary.RiskFlags.Should().NotBeNull();
        }
    }

    [Fact]
    public void ToLlmPayload_RiskFlags_AreConsistentWithEntryQuality()
    {
        // When entryQuality == Good, the flags must not contradict it.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToSupport: 0.5m, supportStrength: 0.85m,
                distToResistance: null, resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h", distToResistance: null, resistanceStrength: null),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        if (payload.M15.Summary.EntryQuality == "Good")
        {
            // If Good is returned, confirming risk flags must NOT indicate blocking conditions:
            payload.M15.Summary.RiskFlags.Should().NotContain("LowVolume",
                because: "Good entryQuality is incompatible with LowVolume flag");
            payload.M15.Summary.RiskFlags.Should().NotContain("NearResistance",
                because: "Good entryQuality is incompatible with NearResistance flag");
            payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeResistance",
                because: "Good entryQuality is incompatible with NearHigherTimeframeResistance");
            payload.M15.Summary.RiskFlags.Should().NotContain("WeakEntryLevel",
                because: "Good entryQuality is incompatible with WeakEntryLevel flag");
        }
    }

    // ===========================================================================
    // Helpers
    // ===========================================================================

    /// <summary>
    /// Creates a full <see cref="MarketAnalysisSnapshot"/> with overridable TF snapshots.
    /// All TF snapshots default to a neutral, non-constraining state unless overridden.
    /// </summary>
    private static MarketAnalysisSnapshot MakeSnapshot(
        TimeframeAnalysisSnapshot? m15 = null,
        TimeframeAnalysisSnapshot? h1 = null,
        TimeframeAnalysisSnapshot? h4 = null,
        TimeframeAnalysisSnapshot? d1 = null,
        string regime = MarketRegimes.Trending)
    {
        var baseSnapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish);
        return baseSnapshot with
        {
            M15 = m15 ?? MakeNeutralTf("15m"),
            H1 = h1 ?? MakeNeutralTf("1h"),
            H4 = h4 ?? MakeNeutralTf("4h"),
            D1 = d1 ?? MakeNeutralTf("1d"),
            Sentiment = baseSnapshot.Sentiment with { MarketRegime = regime },
        };
    }

    /// <summary>
    /// Creates a bullish timeframe snapshot with good defaults and overridable level distances/volumes.
    /// All indicators are reliable, price is above both EMAs, trend is confirmed.
    /// </summary>
    private static TimeframeAnalysisSnapshot MakeBullishTf(
        string timeframe,
        decimal? distToSupport = 0.5m,
        decimal? supportStrength = 0.80m,
        decimal? distToResistance = 0.5m,
        decimal? resistanceStrength = 0.80m,
        decimal volumeRatio = 1.2m) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 99_800m, High = 100_200m, Low = 99_700m, Close = 100_000m,
                Volume = 1200m, Turnover = 120_000_000m,
            },
            Ema20 = 99_500m,
            Ema50 = 99_200m,
            Ema200 = 96_000m,
            Rsi14 = 60m,
            Rsi14IsReliable = true,
            Atr14 = 200m,
            VolumeSma20 = 1000m,
            VolumeRatio = volumeRatio,
            TrendStrengthScore = 0.85m,
            Trend = MarketTrend.Bullish,
            Support1 = distToSupport.HasValue ? 100_000m * (1m - distToSupport.Value / 100m) : null,
            Support1Strength = distToSupport.HasValue ? supportStrength : null,
            DistanceToSupport1Pct = distToSupport,
            Resistance1 = distToResistance.HasValue ? 100_000m * (1m + distToResistance.Value / 100m) : null,
            Resistance1Strength = distToResistance.HasValue ? resistanceStrength : null,
            DistanceToResistance1Pct = distToResistance,
            IsAboveEma20 = true,
            IsAboveEma50 = true,
            IsAboveEma200 = true,
            EmaBullishAlignment = true,
            EmaBearishAlignment = false,
            RsiOverbought = false, RsiOversold = false,
            EmaIsReliable = true, EmaHasFallback = false,
            AtrIsReliable = true, AtrIsFallback = false,
            VolumeRatioIsReliable = true, VolumeRatioIsFallback = false,
            CandleRangePct = 0.50m,
        };

    /// <summary>
    /// Creates a bearish timeframe snapshot with good defaults and overridable level distances/volumes.
    /// All indicators are reliable, price is below both EMAs, trend is confirmed.
    /// </summary>
    private static TimeframeAnalysisSnapshot MakeBearishTf(
        string timeframe,
        decimal? distToResistance = 0.5m,
        decimal? resistanceStrength = 0.80m,
        decimal? distToSupport = 0.5m,
        decimal? supportStrength = 0.80m,
        decimal volumeRatio = 1.2m) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 100_200m, High = 100_300m, Low = 99_800m, Close = 100_000m,
                Volume = 1200m, Turnover = 120_000_000m,
            },
            Ema20 = 100_500m,
            Ema50 = 100_800m,
            Ema200 = 104_000m,
            Rsi14 = 38m,
            Rsi14IsReliable = true,
            Atr14 = 200m,
            VolumeSma20 = 1000m,
            VolumeRatio = volumeRatio,
            TrendStrengthScore = 0.85m,
            Trend = MarketTrend.Bearish,
            Resistance1 = distToResistance.HasValue ? 100_000m * (1m + distToResistance.Value / 100m) : null,
            Resistance1Strength = distToResistance.HasValue ? resistanceStrength : null,
            DistanceToResistance1Pct = distToResistance,
            Support1 = distToSupport.HasValue ? 100_000m * (1m - distToSupport.Value / 100m) : null,
            Support1Strength = distToSupport.HasValue ? supportStrength : null,
            DistanceToSupport1Pct = distToSupport,
            IsAboveEma20 = false,
            IsAboveEma50 = false,
            IsAboveEma200 = false,
            EmaBullishAlignment = false,
            EmaBearishAlignment = true,
            RsiOverbought = false, RsiOversold = false,
            EmaIsReliable = true, EmaHasFallback = false,
            AtrIsReliable = true, AtrIsFallback = false,
            VolumeRatioIsReliable = true, VolumeRatioIsFallback = false,
            CandleRangePct = 0.50m,
        };

    /// <summary>
    /// Creates a neutral/sideways snapshot � does not constrain or help any TF's entryQuality.
    /// </summary>
    private static TimeframeAnalysisSnapshot MakeNeutralTf(string timeframe) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 100_000m, High = 100_200m, Low = 99_800m, Close = 100_000m,
                Volume = 1000m, Turnover = 100_000_000m,
            },
            Ema20 = 100_000m, Ema50 = 100_000m, Ema200 = 100_000m,
            Rsi14 = 50m, Rsi14IsReliable = true,
            Atr14 = 200m,
            VolumeSma20 = 1000m, VolumeRatio = 1.0m,
            TrendStrengthScore = 0.3m,
            Trend = MarketTrend.Sideways,
            // No levels � will not act as a higher-TF obstacle
            Support1 = null, Support1Strength = null, DistanceToSupport1Pct = null,
            Resistance1 = null, Resistance1Strength = null, DistanceToResistance1Pct = null,
            IsAboveEma20 = true, IsAboveEma50 = true, IsAboveEma200 = true,
            EmaBullishAlignment = false, EmaBearishAlignment = false,
            RsiOverbought = false, RsiOversold = false,
            EmaIsReliable = true, EmaHasFallback = false,
            AtrIsReliable = true, AtrIsFallback = false,
            VolumeRatioIsReliable = true, VolumeRatioIsFallback = false,
            CandleRangePct = 0.20m,
        };
}
