using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Интеграционные тесты уровня mapper для <c>LlmPayloadMapperExtensions.ToLlmPayload</c>.
///
/// Эти тесты проверяют:
/// 1. Уровни противоположного направления со старшего таймфрейма корректно передаются в сводку младшего таймфрейма (связь между таймфреймами).
/// 2. EntryQuality.Good недостижимо при слабых или противоречивых условиях во всём конвейере.
/// 3. EntryQuality.Good по-прежнему достижимо в чистых сценариях во всём конвейере.
/// 4. Структура JSON не изменяется: меняются только ожидаемые значения entryQuality.
/// 5. riskFlags согласованы с entryQuality (противоречия отсутствуют).
///
/// Тесты проходят через <c>ToLlmPayload</c> тем же путём, что и реальная конечная точка API.
/// </summary>
public sealed class LlmPayloadMapperExtensionsTests
{
    // ---    health ---------------------------------------------

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
    // Передача уровней между TF: M15 учитывает противоположные уровни H1/H4
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4HasVeryCloseResistance_M15EntryQualityIsPoor()
    {
        // Подготовка: M15 bullish, хорошие условия, локального сопротивления нет.
        // У H4 есть очень близкое сильное сопротивление (0.05%) → M15 должен получить Poor.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToResistance: null,       // локального сопротивления M15 нет
                resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h",
                distToResistance: 0.05m,      // очень близкое сопротивление H4
                resistanceStrength: 0.85m),   // сильное
            regime: MarketRegimes.Trending);

        // Действие
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        // Проверка
        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "H4 strong resistance at 0.05% < 0.15% threshold must force M15 entryQuality to Poor");
        payload.M15.Summary.RiskFlags.Should().Contain("NearHigherTimeframeResistance",
            because: "NearHigherTimeframeResistance flag must be set when H4 resistance is very close");
    }

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4HasNearResistance_M15EntryQualityIsNotGood()
    {
        // Сопротивление H4 на 0.20% (< 0.30%) → Good запрещён для M15.
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
        // Сопротивление H4 на 0.50% (>= 0.30%) → ограничений от старшего TF нет.
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
        // Сопротивление D1 на 0.20% для H1 → Good запрещён.
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
        // M15 bearish, у H4 есть очень близкая сильная поддержка (0.05%) → Poor.
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
        // Если у старшего TF расстояние до support/resistance равно null (уровень отсутствует) → ограничений нет.
        var snapshot = MakeSnapshot(
            m15: MakeBullishTf("15m",
                distToResistance: null,
                resistanceStrength: null,
                volumeRatio: 1.2m),
            h4: MakeBullishTf("4h",
                // Смоделировать сопротивление ниже цены: null (позади сделки).
                distToResistance: null,
                resistanceStrength: 0.85m),
            regime: MarketRegimes.Trending);

        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Good",
            because: "H4 resistance with null distance (wrong side) must not constrain M15 entryQuality");
        payload.M15.Summary.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    // ===========================================================================
    // Регрессионные сценарии в стиле BTCUSDT через полный pipeline
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_BtcUsdtLike_M15Bullish_LowVolume_BelowEmas_NeutralRegime_NearH4Resistance_IsPoor()
    {
        // Сценарий BTCUSDT для M15:
        // - Бычий bias, но цена ниже обеих EMA
        // - Низкий объём (0.1971)
        // - Устаревший snapshot
        // - Нейтральный market regime
        // - У H4 есть близкое сильное сопротивление на 0.05% выше цены
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
            Ema20 = 77_600m,    // EMA выше close → isAboveEma20 = false
            Ema50 = 77_550m,    // EMA выше close → isAboveEma50 = false
            Ema200 = 75_000m,
            Rsi14 = 45m,
            Rsi14IsReliable = true,
            Atr14 = 200m,
            VolumeSma20 = 1000m,
            VolumeRatio = 0.1971m,             // очень низкий объём
            TrendStrengthScore = 0.6m,
            Trend = MarketTrend.Bullish,
            Support1 = 77_000m,
            Support1Strength = 0.50m,           // умеренная поддержка
            DistanceToSupport1Pct = 0.56m,
            Resistance1 = null,                 // сопротивления M15 нет
            Resistance1Strength = null,
            DistanceToResistance1Pct = null,
            IsAboveEma20 = false,               // конфликт EMA
            IsAboveEma50 = false,               // конфликт EMA
            IsAboveEma200 = true,
            EmaBullishAlignment = true,
            EmaBearishAlignment = false,
            RsiOverbought = false, RsiOversold = false,
            EmaIsReliable = true, EmaHasFallback = false,
            AtrIsReliable = true, AtrIsFallback = false,
            VolumeRatioIsReliable = true, VolumeRatioIsFallback = false,
            CandleRangePct = 0.19m,
        };

        // У H4 есть близкое сильное сопротивление на 0.05% выше текущей цены m15
        var h4 = MakeBullishTf("4h",
            distToResistance: 0.05m,
            resistanceStrength: 0.80m,
            volumeRatio: 1.0m);

        var snapshot = MakeSnapshot(m15: m15, h4: h4, regime: MarketRegimes.Neutral);
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _staleHealth);

        payload.M15.Summary.EntryQuality.Should().Be("Poor",
            because: "BTCUSDT m15: very low volume + EMA conflict + stale snapshot + " +
                     "neutral regime + very close H4 resistance > Poor");
        // Проверить, что исходные поля не изменены
        payload.M15.VolumeRatio.Should().Be(0.1971m);
        payload.M15.IsAboveEma20.Should().BeFalse();
        payload.M15.IsAboveEma50.Should().BeFalse();
    }

    [Fact]
    public void ToLlmPayload_BtcUsdtLike_H4Bearish_VeryLowVolume_PriceAboveBothEmas_NeutralRegime_IsNotGood()
    {
        // Сценарий BTCUSDT для H4:
        // - Медвежий bias, но цена выше EMA (конфликт EMA)
        // - очень низкое значение window (0.0184)
        // - нейтральный режим
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
            Ema20 = 101_500m,   // EMA ниже close → isAboveEma20 = true → конфликт для bearish
            Ema50 = 101_400m,   // EMA ниже close → isAboveEma50 = true → конфликт для bearish
            Ema200 = 103_000m,
            Rsi14 = 52m,
            Rsi14IsReliable = true,
            Atr14 = 500m,
            VolumeSma20 = 1000m,
            VolumeRatio = 0.0184m,              // крайне низкий объём
            TrendStrengthScore = 0.7m,
            Trend = MarketTrend.Bearish,
            Resistance1 = 102_000m,
            Resistance1Strength = 0.80m,        // сильное сопротивление
            DistanceToResistance1Pct = 0.3m,
            Support1 = 100_000m,
            Support1Strength = 0.60m,
            DistanceToSupport1Pct = 1.8m,
            IsAboveEma20 = true,                // конфликт EMA для bearish
            IsAboveEma50 = true,                // конфликт EMA для bearish
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
        // Проверить, что исходные данные не изменены
        payload.H4.VolumeRatio.Should().Be(0.0184m);
        payload.H4.IsAboveEma20.Should().BeTrue();
        payload.H4.IsAboveEma50.Should().BeTrue();
    }

    // ===========================================================================
    // Чистые сценарии — Good по-прежнему должен быть достижим
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_CleanBullishSetup_ReturnsGoodForM15()
    {
        // Чистый bullish-сценарий:
        // - Цена выше EMA20/EMA50, тренд подтверждён
        // - Рядом находится сильная поддержка
        // - На текущем и старших TF нет сопротивления
        // - Высокий объём, свежий снимок, трендовый режим
        var m15 = MakeBullishTf("15m",
            distToSupport: 0.5m,
            supportStrength: 0.85m,     // сильная
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
        // Чистый bearish-сценарий:
        // - Цена ниже EMA20/EMA50, тренд подтверждён
        // - Рядом находится сильное сопротивление
        // - На текущем и старших TF нет поддержки (D1)
        // - Высокий объём, свежий снимок, трендовый режим
        var h4 = MakeBearishTf("4h",
            distToResistance: 0.4m,
            resistanceStrength: 0.85m,   // сильное
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
    // ResolveHigherTfOppositeLevel — граничные условия расстояния
    // dist == 0 допустим (препятствие точно на цене); dist < 0 означает неправильную сторону (игнорируется)
    // TODO: интеграционное покрытие mapper для TrendConfirmedButEntryFiltered при dist==0
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_M15Bullish_WhenH4ResistanceDistanceIsZero_ForcesEntryQualityToPoor()
    {
        // Сопротивление H4 точно на цене (distance=0) — ближайшее возможное препятствие.
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
        // Поддержка H4 точно на цене (distance=0) — ближайшее возможное препятствие для bearish.
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
        // Сопротивление H4 с отрицательным расстоянием находится позади сделки (неправильная сторона) → игнорируется.
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
        // Поддержка H4 с отрицательным расстоянием находится позади сделки (неправильная сторона) → игнорируется.
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
        // H1: distance=0 (на цене), H4: distance=0.25 — ноль должен победить как ближайшее препятствие.
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
        // H1: distance=-0.1 (неправильная сторона, игнорируется), H4: distance=0.25 — допустимо только положительное значение.
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
    // Целостность pipeline — исходные market data нельзя изменять
    // ===========================================================================

    [Fact]
    public void ToLlmPayload_RawMarketDataFields_AreNotAlteredByEntryQualityLogic()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish);
        var payload = snapshot.ToLlmPayload(AnalysisMode.Intraday, _freshHealth);

        // Исходные поля индикаторов должны передаваться без изменений
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

        // Версия schema и структурные поля сохраняются
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

        // Поля summary присутствуют в каждом timeframe
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
        // При entryQuality == Good flags не должны ему противоречить.
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
            // Если возвращён Good, подтверждающие risk flags НЕ должны указывать на блокирующие условия:
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
    // Strong
    // ===========================================================================

    /// <summary>
    /// Создаёт полный <see cref="MarketSnapshot"/> с переопределяемыми снимками таймфреймов.
    /// Все снимки таймфреймов по умолчанию находятся в нейтральном состоянии без ограничений, если не переопределены.
    /// </summary>
    private static MarketSnapshot MakeSnapshot(
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
    /// Создаёт снимок бычьего таймфрейма с подходящими значениями по умолчанию и переопределяемыми расстояниями до уровней и объёмами.
    /// Все индикаторы надёжны, цена выше обеих EMA, тренд подтверждён.
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
    /// Создаёт снимок медвежьего таймфрейма с подходящими значениями по умолчанию и переопределяемыми расстояниями до уровней и объёмами.
    /// Все индикаторы надёжны, цена ниже обеих EMA, тренд подтверждён.
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
    /// Создаёт нейтральный/боковой снимок — он не ограничивает и не улучшает entryQuality ни одного таймфрейма.
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
            // Уровней нет — они не должны выступать препятствием старшего TF
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
