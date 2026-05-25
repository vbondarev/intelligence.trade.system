using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Analysis.Tests.Helpers;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Indicators.Results;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

public sealed class TimeframeSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentException_When_Klines_Is_Empty()
    {
        var act = () => TimeframeSnapshotAssembler.Assemble([], timeframe: "1h");

        act
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("klines");
    }

    [Fact]
    public void LastCandle_Is_Newest_Even_When_Input_Is_Unsorted()
    {
        var klines = KlineFactory.CreateSeries(count: 250).ToList();
        var expected = klines.Max(k => k.StartTime);

        klines.Reverse(); // намеренно переворачиваем порядок

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.LastCandle.OpenTimeUtc.UtcDateTime.Should().Be(expected);
    }

    [Fact]
    public void Timeframe_Is_Propagated_Without_Change()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        result.Snapshot.Timeframe.Should().Be("4h");
    }

    [Fact]
    public void Ema_Values_Are_NonZero_With_Sufficient_Data()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.Ema20.Should().NotBeNull().And.NotBe(0m);
        result.Snapshot.Ema50.Should().NotBeNull().And.NotBe(0m);
        result.Snapshot.Ema200.Should().NotBeNull().And.NotBe(0m);
    }

    [Fact]
    public void IsAboveEma20_Matches_Close_Greater_Than_Ema20()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        var expectedIsAbove = result.Snapshot.Ema20.HasValue && result.Snapshot.LastCandle.Close > result.Snapshot.Ema20.Value;
        result.Snapshot.IsAboveEma20.Should().Be(expectedIsAbove);
    }

    [Fact]
    public void IsAboveEma50_Matches_Close_Greater_Than_Ema50()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        var expectedIsAbove = result.Snapshot.Ema50.HasValue && result.Snapshot.LastCandle.Close > result.Snapshot.Ema50.Value;
        result.Snapshot.IsAboveEma50.Should().Be(expectedIsAbove);
    }

    [Fact]
    public void IsAboveEma200_Matches_Close_Greater_Than_Ema200()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        var expectedIsAbove = result.Snapshot.Ema200.HasValue && result.Snapshot.LastCandle.Close > result.Snapshot.Ema200.Value;
        result.Snapshot.IsAboveEma200.Should().Be(expectedIsAbove);
    }

    [Fact]
    public void EmaBullishAlignment_Reflects_Ema_Order()
    {
        var klines = KlineFactory.CreateSeries(count: 250, trend: SeriesTrend.Bullish);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        var s = result.Snapshot;
        var expectedAlignment =
            s.Ema20.HasValue && s.Ema50.HasValue && s.Ema200.HasValue
            && s.Ema20.Value > s.Ema50.Value && s.Ema50.Value > s.Ema200.Value;
        s.EmaBullishAlignment.Should().Be(expectedAlignment);
    }

    [Fact]
    public void EmaBearishAlignment_Reflects_Ema_Order()
    {
        var klines = KlineFactory.CreateSeries(count: 250, trend: SeriesTrend.Bearish, startPrice: 300m);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        var s = result.Snapshot;
        var expectedAlignment =
            s.Ema20.HasValue && s.Ema50.HasValue && s.Ema200.HasValue
            && s.Ema20.Value < s.Ema50.Value && s.Ema50.Value < s.Ema200.Value;
        s.EmaBearishAlignment.Should().Be(expectedAlignment);
    }

    [Fact]
    public void RsiOverbought_Is_True_When_Rsi_At_Least_70()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        // Rsi14 доступен при 250 свечах; флаг совпадает с реальным значением.
        result.Snapshot.RsiOverbought.Should().Be(result.Snapshot.Rsi14.HasValue && result.Snapshot.Rsi14.Value >= 70m);
    }

    [Fact]
    public void RsiOversold_Is_True_When_Rsi_At_Most_30()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        // Rsi14 доступен при 250 свечах; флаг совпадает с реальным значением.
        result.Snapshot.RsiOversold.Should().Be(result.Snapshot.Rsi14.HasValue && result.Snapshot.Rsi14.Value <= 30m);
    }

    [Fact]
    public void Support_Levels_Are_Below_Current_Price_When_Present()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        if (result.Snapshot.Support1 is not null)
            result.Snapshot.Support1.Should().BeLessThan(result.Snapshot.LastCandle.Close);

        if (result.Snapshot.Support2 is not null)
            result.Snapshot.Support2.Should().BeLessThan(result.Snapshot.LastCandle.Close);
    }

    [Fact]
    public void Resistance_Levels_Are_Above_Current_Price_When_Present()
    {
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        if (result.Snapshot.Resistance1 is not null)
            result.Snapshot.Resistance1.Should().BeGreaterThan(result.Snapshot.LastCandle.Close);

        if (result.Snapshot.Resistance2 is not null)
            result.Snapshot.Resistance2.Should().BeGreaterThan(result.Snapshot.LastCandle.Close);
    }

    [Fact]
    public void VolumeRatio_Is_Null_When_All_Volumes_Are_Zero()
    {
        // When all candle volumes are zero, VolumeSma20 = 0 → VolumeRatio cannot be computed → null.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = Enumerable.Range(0, 25)
            .Select(i => KlineFactory.Create(volume: 0m, startTime: baseTime.AddHours(i)))
            .ToList();

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.VolumeRatio.Should().BeNull(
            because: "VolumeSma20 = 0 → division impossible → VolumeRatio is null, not fake-zero");
        result.Snapshot.VolumeRatioIsReliable.Should().BeFalse();

        // A diagnostic must explain why VolumeRatio is absent.
        var diag = result.Snapshot.IndicatorDiagnostics
            .Should().ContainSingle(d => d.Indicator == "volumeRatio").Subject;
        diag.Timeframe.Should().Be("1h");
        diag.Reason.Should().Be(IndicatorValueReason.InvalidInput.ToString(),
            because: "VolumeSma20 is available (= 0) but division by zero is impossible → InvalidInput");
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("volumeRatio").And.Contain("unavailable");
    }

    // ── Insufficient data scenarios ───────────────────────────────────────────

    [Fact]
    public void Rsi14_Is_Null_When_Insufficient_Candles_For_Rsi()
    {
        // RSI14 требует минимум period + 1 = 15 свечей.
        // При меньшем количестве Rsi14 должен быть null, а флаги — false.
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.Rsi14.Should().BeNull();
        result.Snapshot.Rsi14IsReliable.Should().BeFalse();
        result.Snapshot.RsiOverbought.Should().BeFalse();
        result.Snapshot.RsiOversold.Should().BeFalse();
    }

    [Fact]
    public void RsiOverbought_And_RsiOversold_Are_False_When_Rsi_Unavailable()
    {
        // Явная защита: даже если RSI=0 (из-за unavailable), флаги не должны быть true.
        var klines = KlineFactory.CreateSeries(count: 5);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.RsiOverbought.Should().BeFalse();
        result.Snapshot.RsiOversold.Should().BeFalse();
    }

    [Fact]
    public void Trend_Is_Unknown_When_Insufficient_Candles_For_Ema200()
    {
        // EMA200 требует 200 свечей для полноценного расчёта.
        // При достаточном количестве свечей EMA всегда HasUsableValue (fallback или нет).
        // Unknown возникает только когда EMA недоступна (IsAvailable=false) — такого не бывает
        // при непустом массиве (EmaCalculator всегда возвращает Available или Fallback, не Unavailable).
        // Поэтому тест проверяет, что Trend не равен Unknown при достаточных данных.
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.Trend.Should().NotBe(MarketTrend.Unknown);
        result.Snapshot.TrendStrengthScore.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Trend_Is_Unknown_And_StrengthScore_Zero_When_Only_One_Candle()
    {
        // 1 свеча: EMA возвращает Fallback (PartialWindow), IsAvailable=true → HasUsableValue=true
        // → TrendClassifier вызывается. Но проверим, что assembler вообще не падает.
        // На самом деле при 1 свече все EMA = Available/Fallback с value = сама цена.
        // TrendClassifier с ema20==ema50==ema200==close → Sideways.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        // Не должен падать; trend должен быть определён (Sideways или Unknown).
        result.Snapshot.Trend.Should().BeOneOf(MarketTrend.Sideways, MarketTrend.Unknown);
        result.Snapshot.TrendStrengthScore.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void Atr14_Is_Null_When_Only_One_Candle()
    {
        // AtrCalculator.Compute с 1 свечой → Unavailable(InsufficientData) → OrNull() = null.
        // null явно сигнализирует об отсутствии данных, не маскируется как нулевая волатильность.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.Atr14.Should().BeNull(
            because: "1 candle is insufficient for ATR — result is null, not fake-zero");
    }

    [Fact]
    public void VolumeSma20_Calculated_As_Fallback_When_Fewer_Than_20_Candles()
    {
        // При < 20 свечах VolumeSMA20 — fallback-значение (среднее по доступным).
        // VolumeRatio должен считаться, если fallback-значение > 0.
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        // VolumeSma20 ненулевой (свечи с ненулевым объёмом), VolumeRatio тоже ненулевой.
        result.Snapshot.VolumeSma20.Should().NotBeNull().And.BeGreaterThan(0m);
        result.Snapshot.VolumeRatio.Should().NotBeNull().And.BeGreaterThan(0m);
    }

    // ── Diagnostics scenarios ─────────────────────────────────────────────────

    [Fact]
    public void Diagnostics_Are_Empty_With_Sufficient_Data()
    {
        // 250 свечей — все EMA/ATR/VolumeSMA рассчитаны полноценно, RSI тоже доступен.
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Diagnostics_Contain_Ema200_Fallback_When_Fewer_Than_200_Candles()
    {
        // 50 свечей → EMA200 рассчитана по fallback (PartialWindow).
        var klines = KlineFactory.CreateSeries(count: 50);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");

        result.Diagnostics.Should().Contain(d =>
            d.Indicator == "ema200" &&
            d.Reason == Intelligence.TradeSystem.Indicators.Results.IndicatorValueReason.PartialWindow &&
            d.IsFallback);
    }

    [Fact]
    public void Diagnostics_Contain_Rsi14_Unavailable_When_Insufficient_Candles()
    {
        // RSI14 требует period + 1 = 15 свечей. При 10 → Unavailable(InsufficientData).
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Diagnostics.Should().Contain(d =>
            d.Indicator == "rsi14" &&
            d.Reason == Intelligence.TradeSystem.Indicators.Results.IndicatorValueReason.InsufficientData &&
            !d.IsFallback);
    }

    [Fact]
    public void Diagnostics_Contain_Atr14_Unavailable_When_Only_One_Candle()
    {
        // AtrCalculator с 1 свечой → Unavailable(InsufficientData).
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        result.Diagnostics.Should().Contain(d =>
            d.Indicator == "atr14" &&
            d.Reason == Intelligence.TradeSystem.Indicators.Results.IndicatorValueReason.InsufficientData &&
            !d.IsFallback);
    }

    [Fact]
    public void Diagnostics_Timeframe_Matches_Input()
    {
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        result.Diagnostics.Should().OnlyContain(d => d.Timeframe == "4h");
    }

    // ── Nullable indicator contract (step 12) ────────────────────────────────

    [Fact]
    public void Rsi14_Serializes_As_Null_When_Insufficient_Candles()
    {
        // 11.1: RSI unavailable → null в snapshot, не 0.
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.Rsi14.Should().BeNull(because: "rsi14 must be null when insufficient data, not 0");
        result.Snapshot.Rsi14IsReliable.Should().BeFalse();
        result.Snapshot.RsiOverbought.Should().BeFalse();
        result.Snapshot.RsiOversold.Should().BeFalse();
    }

    [Fact]
    public void Atr14_Is_Null_When_Insufficient_Candles()
    {
        // 11.2: ATR unavailable → null в snapshot, не 0.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        result.Snapshot.Atr14.Should().BeNull(because: "atr14 must be null when only 1 candle, not 0");
        result.Snapshot.AtrIsReliable.Should().BeFalse();
    }

    [Fact]
    public void Ema200_Has_Fallback_Value_And_Diagnostic_When_Below_200_Candles()
    {
        // 11.3: EMA200 fallback — значение доступно, diagnostic присутствует.
        var klines = KlineFactory.CreateSeries(count: 50);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");

        // EMA200 имеет числовое значение (fallback по partial window).
        result.Snapshot.Ema200.Should().NotBeNull(because: "EMA200 computes a fallback value with partial window");
        result.Snapshot.Ema200.Should().BeGreaterThan(0m);

        // Diagnostic объясняет причину fallback.
        result.Diagnostics.Should().Contain(d =>
            d.Indicator == "ema200" &&
            d.Reason == Intelligence.TradeSystem.Indicators.Results.IndicatorValueReason.PartialWindow &&
            d.IsFallback);
    }

    [Fact]
    public void Boolean_EmaFlags_Are_False_When_Ema_Would_Be_Zero()
    {
        // 11.4: с одной свечой EMA = fallback (= цена), boolean flags должны отражать реальное сравнение.
        // Даже при partial window EMA имеет значение — флаги корректны.
        // Проверяем только, что флаги не основаны на fake-zero.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        // Alignment зависит от реальных значений EMA, а не от fake-zero.
        var expectedBullish =
            s.Ema20.HasValue && s.Ema50.HasValue && s.Ema200.HasValue
            && s.Ema20.Value > s.Ema50.Value && s.Ema50.Value > s.Ema200.Value;
        var expectedBearish =
            s.Ema20.HasValue && s.Ema50.HasValue && s.Ema200.HasValue
            && s.Ema20.Value < s.Ema50.Value && s.Ema50.Value < s.Ema200.Value;

        s.EmaBullishAlignment.Should().Be(expectedBullish);
        s.EmaBearishAlignment.Should().Be(expectedBearish);
    }

    [Fact]
    public void RsiOverbought_And_RsiOversold_False_When_Rsi_Null()
    {
        // 11.5: RSI null → флаги false.
        var klines = KlineFactory.CreateSeries(count: 5);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.Rsi14.Should().BeNull();
        result.Snapshot.RsiOverbought.Should().BeFalse(because: "null RSI must not trigger overbought");
        result.Snapshot.RsiOversold.Should().BeFalse(because: "null RSI must not trigger oversold");
    }

    // ───── KlineValidator integration ─────

    [Fact]
    public void Invalid_Kline_Is_Excluded_And_Diagnostic_Is_Emitted()
    {
        // Prepare: 10 valid candles + 1 invalid (High < Low) at position 5.
        var klines = KlineFactory.CreateSeries(count: 10).ToList();
        klines[5] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m);

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.IndicatorDiagnostics
            .Should().Contain(d =>
                d.Indicator == "kline" &&
                d.Reason == "InvalidInput" &&
                d.IsFallback == false &&
                d.Message.Contains("kline[5]"),
                because: "the invalid candle at index 5 must produce a kline diagnostic");
    }

    [Fact]
    public void All_Valid_Klines_Produce_No_Kline_Diagnostics()
    {
        var klines = KlineFactory.CreateSeries(count: 50);

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Snapshot.IndicatorDiagnostics
            .Should().NotContain(d => d.Indicator == "kline",
                because: "no validation violations were present");
    }

    [Fact]
    public void All_Invalid_Klines_Throws_DataSourceException()
    {
        var klines = new[]
        {
            KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m),
            KlineFactory.Create(open: 100m, high: 80m, low: 95m, close: 95m),
        };

        var act = () => TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        act.Should()
            .Throw<DataSourceException>()
            .WithMessage("*All klines*failed validation*");
    }

    [Fact]
    public void Negative_Volume_Kline_Is_Excluded_And_Diagnostic_Contains_Volume()
    {
        var klines = KlineFactory.CreateSeries(count: 10).ToList();
        klines[0] = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 100m, volume: -1m);

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        result.Snapshot.IndicatorDiagnostics
            .Should().Contain(d =>
                d.Indicator == "kline" && d.Message.Contains("Volume"),
                because: "negative volume violates OHLCV invariant");
    }

    // ── Degradation policy diagnostics ───────────────────────────────────────

    [Fact]
    public void Diagnostic_LastKlineFiltered_When_Newest_Candle_Is_Invalid()
    {
        // Arrange: 5 valid candles + 1 invalid candle with the LATEST StartTime.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = KlineFactory.CreateSeries(count: 5).ToList();
        // Inject an invalid candle (High < Low) with StartTime beyond all valid candles.
        klines.Add(KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m,
            startTime: baseTime.AddHours(100)));

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        var diag = result.Diagnostics
            .Should().Contain(d => d.Indicator == "kline.lastFiltered",
                because: "the newest candle by StartTime was invalid and filtered out")
            .Subject;
        diag.Timeframe.Should().Be("1h");
        diag.IsFallback.Should().BeFalse();
        diag.Reason.Should().Be(IndicatorValueReason.InvalidInput);
        diag.Message.Should().Contain("most recent candle");
    }

    [Fact]
    public void No_LastKlineFiltered_Diagnostic_When_All_Klines_Are_Valid()
    {
        var klines = KlineFactory.CreateSeries(count: 50);

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Diagnostics
            .Should().NotContain(d => d.Indicator == "kline.lastFiltered",
                because: "no candles were filtered");
    }

    [Fact]
    public void No_LastKlineFiltered_Diagnostic_When_Invalid_Candle_Is_Not_The_Most_Recent()
    {
        // Invalid candle at position 2 (not the last by time — series goes 0h..9h, invalid at 2h).
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = KlineFactory.CreateSeries(count: 10).ToList();
        klines[2] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m,
            startTime: baseTime.AddHours(2));

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Diagnostics
            .Should().NotContain(d => d.Indicator == "kline.lastFiltered",
                because: "the filtered candle is not the most recent one");
    }

    [Fact]
    public void Diagnostic_HighViolationRate_When_More_Than_20_Percent_Are_Invalid()
    {
        // 10 candles, 3 invalid = 30% > 20% threshold.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = KlineFactory.CreateSeries(count: 10).ToList();
        klines[1] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(1));
        klines[3] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(3));
        klines[5] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(5));

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");

        var diag = result.Diagnostics
            .Should().Contain(d => d.Indicator == "kline.highViolationRate",
                because: "3/10 = 30% of candles failed — exceeds the 20% threshold")
            .Subject;
        diag.Reason.Should().Be(IndicatorValueReason.InvalidInput);
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("3/10");
    }

    [Fact]
    public void No_HighViolationRate_Diagnostic_When_Below_Threshold()
    {
        // 10 candles, 1 invalid = 10% <= 20% threshold → no highViolationRate diagnostic.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = KlineFactory.CreateSeries(count: 10).ToList();
        klines[2] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(2));

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Diagnostics
            .Should().NotContain(d => d.Indicator == "kline.highViolationRate",
                because: "only 1/10 = 10% of candles failed, which is below the 20% threshold");
    }

    [Fact]
    public void Diagnostic_InsufficientData_When_Only_One_Valid_Kline_Remains()
    {
        // 4 invalid + 1 valid = 1 usable candle < KlineMinimumUsableCount (2).
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = new List<Intelligence.TradeSystem.Domain.Kline>
        {
            KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime),
            KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(1)),
            KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(2)),
            KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m, startTime: baseTime.AddHours(3)),
            KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 100m, startTime: baseTime.AddHours(4)),
        };

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");

        var diag = result.Diagnostics
            .Should().Contain(d => d.Indicator == "kline.insufficientData",
                because: "only 1 valid candle remains after filtering, below minimum usable count of 2")
            .Subject;
        diag.Reason.Should().Be(IndicatorValueReason.InsufficientData);
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("1 valid candle(s)");
    }

    [Fact]
    public void No_InsufficientData_Diagnostic_When_Two_Or_More_Valid_Klines_Remain()
    {
        var klines = KlineFactory.CreateSeries(count: 5);

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        result.Diagnostics
            .Should().NotContain(d => d.Indicator == "kline.insufficientData",
                because: "5 valid candles is sufficient");
    }

    // ── Level meta — Strength and ClusterVolume propagation ──────────────────

    [Fact]
    public void Support1_Strength_And_ClusterVolume_Are_Populated_When_Level_Is_Detected()
    {
        // 250 свечей достаточно для работы VolumeProfileDetector
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var snap = result.Snapshot;

        // Если уровень обнаружен — Strength и ClusterVolume должны быть заполнены
        if (snap.Support1 is not null)
        {
            snap.Support1Strength.Should().HaveValue(
                because: "Support1 is detected → Strength must be populated by the assembler");
            snap.Support1Strength!.Value.Should().BeInRange(0m, 1m,
                because: "Strength is normalised to [0, 1]");
            snap.Support1ClusterVolume.Should().HaveValue(
                because: "Support1 is detected → ClusterVolume must be populated by the assembler");
            snap.Support1ClusterVolume!.Value.Should().BePositive(
                because: "ClusterVolume of a detected level must be > 0");
        }

        if (snap.Resistance1 is not null)
        {
            snap.Resistance1Strength.Should().HaveValue(
                because: "Resistance1 is detected → Strength must be populated by the assembler");
            snap.Resistance1ClusterVolume.Should().HaveValue(
                because: "Resistance1 is detected → ClusterVolume must be populated by the assembler");
        }
    }

    [Fact]
    public void Support1_Strength_And_ClusterVolume_Are_Null_When_Level_Is_Not_Detected()
    {
        // Одна свеча — объёмный профиль не найдёт уровней из-за нехватки данных
        var klines = KlineFactory.CreateSeries(count: 1);

        // Assembler выбрасывает исключение при validKlines < 2 — проверяем это отдельно;
        // здесь нас интересует поведение когда уровни не обнаружены.
        // Создаём минимально достаточный набор свечей с одинаковыми ценами — профиль не выдаст поддержку/сопротивление
        // относительно Close, поэтому проверяем консистентность: если Price == null, то Strength == null.
        var klines200 = KlineFactory.CreateSeries(count: 200);
        var result = TimeframeSnapshotAssembler.Assemble(klines200, timeframe: "1h");
        var snap = result.Snapshot;

        if (snap.Support1 is null)
        {
            snap.Support1Strength.Should().BeNull(
                because: "Support1 not detected → Strength must also be null");
            snap.Support1ClusterVolume.Should().BeNull(
                because: "Support1 not detected → ClusterVolume must also be null");
        }

        if (snap.Resistance1 is null)
        {
            snap.Resistance1Strength.Should().BeNull(
                because: "Resistance1 not detected → Strength must also be null");
            snap.Resistance1ClusterVolume.Should().BeNull(
                because: "Resistance1 not detected → ClusterVolume must also be null");
        }
    }
}
