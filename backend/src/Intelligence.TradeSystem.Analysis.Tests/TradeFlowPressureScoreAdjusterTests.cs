using FluentAssertions;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests;

/// <summary>
/// Unit-тесты для <see cref="TradeFlowPressureScoreAdjuster"/>.
///
/// Проверяемые сценарии:
///   1.  Stale tradeFlow (age > maxAge)                  → score ≤ 0.50
///   2.  Very stale (age > maxAge × 2)                   → score ≤ 0.25
///   3.  Window &lt; 10 s                                 → score ≤ 0.25
///   4.  Window ∈ [10, 30) s                             → score ≤ 0.35
///   5.  Window ∈ [30, 60) s                             → score ≤ 0.50
///   6.  Total volume &lt; 1 BTC                          → score ≤ 0.35
///   7.  Total volume ∈ [1, 3) BTC                       → score ≤ 0.50
///   8.  Conflict: orderBookScore &lt; 0                   → score ≤ 0.50
///   9.  Conflict + short window (&lt; 30 s)              → score ≤ 0.25
///   10. Negative raw score with caps                     → sign preserved
///   11. Raw score below active cap                       → score unchanged
///   12. Clean scenario: all conditions green             → score unchanged
///   13. Composition: strictest cap wins
///   14. Regression: BTCUSDT-like stale + short + conflict → score ≤ 0.25
/// </summary>
public sealed class TradeFlowPressureScoreAdjusterTests
{
    private const long DefaultMaxAgeMs = TradeFlowPressureScoreAdjuster.DefaultMaxTradeFlowAgeMs; // 5_000

    // --- 1. Stale tradeFlow ---------------------------------------------------

    [Fact]
    public void Stale_TradeFlow_Caps_Score_At_0_50()
    {
        // age = 7 000 ms > maxAge (5 000 ms) but < maxAge * 2 (10 000 ms) → StaleCap = 0.50
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateTradeFlow(
            windowEnd: now.AddMilliseconds(-7_000),
            windowStart: now.AddMilliseconds(-7_000 - 300_000),
            buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now,
            maxTradeFlowAgeMs: DefaultMaxAgeMs);

        result.Should().BeLessThanOrEqualTo(0.50m);
    }

    // --- 2. Very stale --------------------------------------------------------

    [Fact]
    public void Very_Stale_TradeFlow_Caps_Score_At_0_25()
    {
        // age = 25 000 ms > maxAge * 2 (10 000 ms) → VeryStaleCap = 0.25
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateTradeFlow(
            windowEnd: now.AddMilliseconds(-25_000),
            windowStart: now.AddMilliseconds(-25_000 - 300_000),
            buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now,
            maxTradeFlowAgeMs: DefaultMaxAgeMs);

        result.Should().BeLessThanOrEqualTo(0.25m);
    }

    // --- 3. Window < 10 s -----------------------------------------------------

    [Fact]
    public void Window_Under_10s_Caps_Score_At_0_25()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 8, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.25m);
    }

    // --- 4. Window [10, 30) s -------------------------------------------------

    [Fact]
    public void Window_10_To_30s_Caps_Score_At_0_35()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 20, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.35m);
    }

    // --- 5. Window [30, 60) s -------------------------------------------------

    [Fact]
    public void Window_30_To_60s_Caps_Score_At_0_50()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 45, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.50m);
    }

    // --- 6. Volume < 1 BTC ---------------------------------------------------

    [Fact]
    public void Volume_Under_1_BTC_Caps_Score_At_0_35()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 300, buyVolume: 0.872m, sellVolume: 0.1m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.35m);
    }

    // --- 7. Volume [1, 3) BTC ------------------------------------------------

    [Fact]
    public void Volume_1_To_3_BTC_Caps_Score_At_0_50()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 300, buyVolume: 1.5m, sellVolume: 0.8m);
        // total = 2.3 BTC ? [1, 3)

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.50m);
    }

    // --- 8. Conflict: obScore < 0 --------------------------------------------

    [Fact]
    public void Conflict_With_OrderBook_Caps_Score_At_0_50()
    {
        var now = DateTimeOffset.UtcNow;
        // Long window, big volume, fresh > only conflict cap applies
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 300, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: -0.5m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.50m);
    }

    // --- 9. Conflict + short window < 30 s -----------------------------------

    [Fact]
    public void Conflict_And_Short_Window_Caps_Score_At_0_25()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 20, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: -0.3m,
            capturedAtUtc: now);

        result.Should().BeLessThanOrEqualTo(0.25m);
    }

    // --- 10. Negative raw score � sign preserved ------------------------------

    [Fact]
    public void Negative_RawScore_Preserves_Sign_After_Cap()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 8, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: -1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().BeNegative();
        result.Should().BeGreaterThanOrEqualTo(-0.25m);
    }

    // --- 11. Raw score below active cap � score unchanged --------------------

    [Fact]
    public void Score_Below_Cap_Is_Not_Changed()
    {
        var now = DateTimeOffset.UtcNow;
        // Window = 8 s > cap = 0.25; raw = 0.2 < 0.25 > unchanged
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 8, buyVolume: 50m, sellVolume: 50m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 0.2m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0m,
            capturedAtUtc: now);

        result.Should().Be(0.2m);
    }

    // --- 12. Clean scenario � no caps apply ----------------------------------

    [Fact]
    public void Clean_Scenario_Allows_Full_Score()
    {
        var now = DateTimeOffset.UtcNow;
        // Fresh, window >= 300 s, volume >= 3 BTC, no conflict
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 300, buyVolume: 10m, sellVolume: 5m);

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0.3m,
            capturedAtUtc: now);

        result.Should().Be(1m);
    }

    // --- 13. Composition: strictest cap wins ---------------------------------

    [Fact]
    public void Strictest_Cap_Wins_When_Multiple_Caps_Apply()
    {
        // stale cap = 0.50, short window (8 s) cap = 0.25, volume (<1) cap = 0.35, conflict cap = 0.50
        // strictest = 0.25
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateTradeFlow(
            windowEnd: now.AddMilliseconds(-12_000),           // stale
            windowStart: now.AddMilliseconds(-12_000 - 8_000), // window = 8 s
            buyVolume: 0.872m, sellVolume: 0.1m);              // total < 1 BTC

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: -0.3m, // conflict
            capturedAtUtc: now,
            maxTradeFlowAgeMs: DefaultMaxAgeMs);

        result.Should().BeLessThanOrEqualTo(0.25m);
    }

    // --- 14. Regression: BTCUSDT-like snapshot -------------------------------

    [Fact]
    public void Regression_BTCUSDT_Like_Stale_Short_Conflict_Caps_At_0_25()
    {
        // buyVolume = 0.872, sellVolume = 0.1
        // deltaPct ? 79% > raw = 1 (clamped after AggressiveBuyPressure floor in assembler)
        // windowDuration ? 8 s
        // tradeFlowAgeMs ? 5824, maxTradeFlowAgeMs = 5000
        // orderBookPressureScore < 0 (AskDominant)
        var maxAgeMs = 5_000L;
        var now = DateTimeOffset.UtcNow;
        var windowEnd = now.AddMilliseconds(-5_824);
        var windowStart = windowEnd.AddSeconds(-8);
        var tradeFlow = CreateTradeFlow(
            windowEnd: windowEnd,
            windowStart: windowStart,
            buyVolume: 0.872m, sellVolume: 0.1m);

        // Caps:
        //   freshness: ageMs=5824 > maxAge=5000 > cap=0.50
        //   window:    8 s < 10 s               > cap=0.25
        //   volume:    0.972 < 1 BTC             > cap=0.35
        //   conflict:  obScore<0, window<30s     > conflictWithWeakness cap=0.25
        // Strictest: 0.25

        var result = TradeFlowPressureScoreAdjuster.ApplyCaps(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: -0.3m,
            capturedAtUtc: now,
            maxTradeFlowAgeMs: maxAgeMs);

        result.Should().BeLessThanOrEqualTo(0.25m, because: "BTCUSDT-regression: stale+short window+conflict");
    }

    // --- ComputeWindowCap unit tests -----------------------------------------

    [Theory]
    [InlineData(0, 0.25)]
    [InlineData(5, 0.25)]
    [InlineData(9.9, 0.25)]
    [InlineData(10, 0.35)]
    [InlineData(29.9, 0.35)]
    [InlineData(30, 0.50)]
    [InlineData(59.9, 0.50)]
    [InlineData(60, 1.0)]
    [InlineData(300, 1.0)]
    public void ComputeWindowCap_Returns_Expected_Cap(double windowSeconds, decimal expectedCap)
    {
        TradeFlowPressureScoreAdjuster.ComputeWindowCap(windowSeconds).Should().Be(expectedCap);
    }

    // --- ComputeVolumeCap unit tests -----------------------------------------

    [Theory]
    [InlineData(0, 0.35)]
    [InlineData(0.5, 0.35)]
    [InlineData(0.999, 0.35)]
    [InlineData(1.0, 0.50)]
    [InlineData(2.999, 0.50)]
    [InlineData(3.0, 1.0)]
    [InlineData(100, 1.0)]
    public void ComputeVolumeCap_Returns_Expected_Cap(decimal totalVolume, decimal expectedCap)
    {
        TradeFlowPressureScoreAdjuster.ComputeVolumeCap(totalVolume).Should().Be(expectedCap);
    }

    // --- HasOrderBookConflict unit tests -------------------------------------

    [Theory]
    [InlineData(1, -0.1, true)]   // tf positive, ob negative > conflict
    [InlineData(-1, 0.1, true)]   // tf negative, ob positive > conflict
    [InlineData(1, 0.1, false)]   // same sign
    [InlineData(-1, -0.1, false)] // same sign
    [InlineData(0, -0.5, false)]  // tf zero > no conflict
    [InlineData(1, 0, false)]     // ob zero > no conflict
    public void HasOrderBookConflict_Detects_Conflict(decimal tfScore, decimal obScore, bool expected)
    {
        TradeFlowPressureScoreAdjuster.HasOrderBookConflict(tfScore, obScore).Should().Be(expected);
    }

    // --- ApplyCapToScore unit tests -------------------------------------------

    [Theory]
    [InlineData(1, 0.25, 0.25)]    // positive, cap applies
    [InlineData(-1, 0.25, -0.25)]  // negative, cap applies with sign
    [InlineData(0.2, 0.25, 0.2)]   // below cap, unchanged
    [InlineData(-0.2, 0.25, -0.2)] // below cap, unchanged (negative)
    [InlineData(0, 0.25, 0)]       // zero stays zero
    public void ApplyCapToScore_Applies_Cap_With_Sign_Preservation(
        decimal rawScore, decimal cap, decimal expected)
    {
        TradeFlowPressureScoreAdjuster.ApplyCapToScore(rawScore, cap).Should().Be(expected);
    }

    // --- Quality tags tests ---------------------------------------------------

    [Fact]
    public void ComputeQualityTags_Returns_Expected_Tags_For_Regression_Scenario()
    {
        var maxAgeMs = 5_000L;
        var now = DateTimeOffset.UtcNow;
        var windowEnd = now.AddMilliseconds(-5_824);
        var windowStart = windowEnd.AddSeconds(-8);
        var tradeFlow = CreateTradeFlow(
            windowEnd: windowEnd,
            windowStart: windowStart,
            buyVolume: 0.872m, sellVolume: 0.1m);

        var tags = TradeFlowPressureScoreAdjuster.ComputeQualityTags(
            rawScore: 1m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: -0.3m,
            capturedAtUtc: now,
            maxTradeFlowAgeMs: maxAgeMs);

        tags.Should().Contain(MarketTagConstants.StaleTradeFlow);
        tags.Should().Contain(MarketTagConstants.ShortTradeFlowWindow);
        tags.Should().Contain(MarketTagConstants.OrderBookTradeFlowConflict);
        tags.Should().Contain(MarketTagConstants.WeakTradeFlowConfirmation);
    }

    [Fact]
    public void ComputeQualityTags_Returns_Empty_For_Clean_Scenario()
    {
        var now = DateTimeOffset.UtcNow;
        var tradeFlow = CreateFreshTradeFlow(now, windowSeconds: 300, buyVolume: 10m, sellVolume: 5m);

        var tags = TradeFlowPressureScoreAdjuster.ComputeQualityTags(
            rawScore: 0.8m,
            tradeFlow: tradeFlow,
            orderBookPressureScore: 0.3m,
            capturedAtUtc: now);

        tags.Should().NotContain(MarketTagConstants.StaleTradeFlow);
        tags.Should().NotContain(MarketTagConstants.ShortTradeFlowWindow);
        tags.Should().NotContain(MarketTagConstants.OrderBookTradeFlowConflict);
        tags.Should().NotContain(MarketTagConstants.WeakTradeFlowConfirmation);
    }

    // --- Helpers --------------------------------------------------------------

    /// <summary>
    /// Создаёт TradeFlowSnapshot, свежий (windowEnd == capturedAtUtc),
    /// с заданными длиной окна и объёмами.
    /// </summary>
    private static TradeFlowSnapshot CreateFreshTradeFlow(
        DateTimeOffset capturedAtUtc,
        double windowSeconds,
        decimal buyVolume,
        decimal sellVolume)
    {
        var windowEnd = capturedAtUtc;
        var windowStart = windowEnd.AddSeconds(-windowSeconds);
        return CreateTradeFlow(windowEnd, windowStart, buyVolume, sellVolume);
    }

    private static TradeFlowSnapshot CreateTradeFlow(
        DateTimeOffset windowEnd,
        DateTimeOffset windowStart,
        decimal buyVolume,
        decimal sellVolume)
    {
        var totalVolume = buyVolume + sellVolume;
        var deltaVolume = buyVolume - sellVolume;
        var deltaPct = totalVolume == 0m ? 0m : deltaVolume / totalVolume * 100m;

        return new TradeFlowSnapshot
        {
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            BuyVolume = buyVolume,
            SellVolume = sellVolume,
            DeltaVolume = deltaVolume,
            DeltaPct = deltaPct,
            TotalTrades = 10,
            BuyTrades = 7,
            SellTrades = 3,
            AvgTradeSize = totalVolume / 10m,
            MaxTradeSize = buyVolume,
            HasAggressiveBuyPressure = deltaPct > 10m,
            HasAggressiveSellPressure = deltaPct < -10m,
        };
    }
}
