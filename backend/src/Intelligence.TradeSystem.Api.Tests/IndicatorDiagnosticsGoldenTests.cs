using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Эталонные интеграционные тесты для LLM payload с unavailable/fallback-индикаторами.
/// Проверяют:
/// - nullable-поля индикаторов сериализуются как <c>null</c>, не как <c>0</c>;
/// - <c>indicatorDiagnostics</c> появляются при unavailable/fallback-индикаторах;
/// - при полном наборе данных <c>indicatorDiagnostics</c> пустой;
/// - JSON-контракт стабилен.
/// </summary>
public sealed class IndicatorDiagnosticsGoldenTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public IndicatorDiagnosticsGoldenTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─── Эталонный тест: недоступный RSI сериализуется как null, а не 0 ───────

    [Fact]
    public async Task LlmPayload_Sets_Rsi14_To_Null_And_Adds_Diagnostic_When_Insufficient_Candles()
    {
        // Snapshot с недоступным RSI (Rsi14 = null, Rsi14IsReliable = false).
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: null, overrideRsiOverbought: false, overrideRsiOversold: false);

        // Добавить diagnostic, объясняющий значение null.
        snapshot = snapshot with
        {
            IndicatorDiagnostics =
            [
                new IndicatorDiagnosticSnapshot
                {
                    Timeframe  = "15m",
                    Indicator  = "rsi14",
                    Reason     = "InsufficientData",
                    IsFallback = false,
                    Message    = "15m.rsi14 unavailable: InsufficientData.",
                },
            ],
        };

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // rsi14 должен быть null, а не 0.
        var rsi = json.RootElement.GetProperty("m15").GetProperty("rsi14");
        rsi.ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable RSI must serialize as null, not 0");

        // rsiOversold/rsiOverbought должны быть false.
        json.RootElement.GetProperty("m15").GetProperty("rsiOversold").GetBoolean()
            .Should().BeFalse();
        json.RootElement.GetProperty("m15").GetProperty("rsiOverbought").GetBoolean()
            .Should().BeFalse();

        // indicatorDiagnostics содержит diagnostic для rsi14.
        var diags = json.RootElement.GetProperty("indicatorDiagnostics");
        diags.ValueKind.Should().Be(JsonValueKind.Array);
        var rsiDiag = diags.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "rsi14");
        rsiDiag.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: "rsi14 diagnostic must appear in indicatorDiagnostics");
        rsiDiag.GetProperty("reason").GetString().Should().Be("InsufficientData");
        rsiDiag.GetProperty("isFallback").GetBoolean().Should().BeFalse();
    }

    // ─── Эталонный тест: недоступный ATR сериализуется как null ────────────────

    [Fact]
    public async Task LlmPayload_Sets_Atr14_To_Null_When_Insufficient_Candles()
    {
        var snapshot = BuildSnapshotWithDiagnostics([
            new IndicatorDiagnosticSnapshot
            {
                Timeframe  = "1h",
                Indicator  = "atr14",
                Reason     = "InsufficientData",
                IsFallback = false,
                Message    = "1h.atr14 unavailable: InsufficientData.",
            },
        ], overrideAtr14: null, overrideAtrIsReliable: false);

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var atr = json.RootElement.GetProperty("h1").GetProperty("atr14");
        atr.ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable ATR must serialize as null, not 0");

        // Diagnostic присутствует.
        var diags = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "atr14");
        diags.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        diags.GetProperty("reason").GetString().Should().Be("InsufficientData");
    }

    // ─── Эталонный тест: частичное окно EMA200 добавляет fallback diagnostic ───

    [Fact]
    public async Task LlmPayload_Keeps_Ema200_Value_And_Adds_FallbackDiagnostic_For_PartialWindow()
    {
        var snapshot = BuildSnapshotWithDiagnostics([
            new IndicatorDiagnosticSnapshot
            {
                Timeframe  = "15m",
                Indicator  = "ema200",
                Reason     = "PartialWindow",
                IsFallback = true,
                Message    = "15m.ema200 calculated using fallback: PartialWindow.",
            },
        ]);

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // ema200 имеет числовое значение (fallback, а не null).
        var ema200 = json.RootElement.GetProperty("m15").GetProperty("ema200");
        ema200.ValueKind.Should().Be(JsonValueKind.Number,
            because: "EMA200 fallback should produce a numeric value, not null");

        // Fallback diagnostic присутствует.
        var diag = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "ema200");
        diag.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        diag.GetProperty("reason").GetString().Should().Be("PartialWindow");
        diag.GetProperty("isFallback").GetBoolean().Should().BeTrue();
    }

    // ─── Эталонный тест: indicatorDiagnostics пуст при полном наборе данных ────

    [Fact]
    public async Task LlmPayload_Has_Empty_IndicatorDiagnostics_When_All_Indicators_Available()
    {
        // Все индикаторы доступны → diagnostics отсутствуют.
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        // В MarketSnapshot IndicatorDiagnostics по умолчанию равен [].

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var diags = json.RootElement.GetProperty("indicatorDiagnostics");
        diags.ValueKind.Should().Be(JsonValueKind.Array);
        diags.GetArrayLength().Should().Be(0,
            because: "no diagnostics expected when all indicators are fully available");
    }

    // ─── Эталонный тест: стабильный порядок indicatorDiagnostics в JSON ────────

    [Fact]
    public async Task LlmPayload_IndicatorDiagnostics_Are_In_Stable_Order_In_Json()
    {
        var snapshot = BuildSnapshotWithDiagnostics([
            new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "ema200", Reason = "PartialWindow", IsFallback = true,  Message = "15m.ema200 calculated using fallback: PartialWindow." },
            new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "rsi14",  Reason = "InsufficientData", IsFallback = false, Message = "15m.rsi14 unavailable: InsufficientData." },
            new IndicatorDiagnosticSnapshot { Timeframe = "1h",  Indicator = "ema200", Reason = "PartialWindow", IsFallback = true,  Message = "1h.ema200 calculated using fallback: PartialWindow." },
        ]);

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var diags = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray().ToList();

        diags.Should().HaveCount(3);

        // Стабильный порядок: 15m ema200 → 15m rsi14 → 1h ema200.
        diags[0].GetProperty("timeframe").GetString().Should().Be("15m");
        diags[0].GetProperty("indicator").GetString().Should().Be("ema200");
        diags[1].GetProperty("timeframe").GetString().Should().Be("15m");
        diags[1].GetProperty("indicator").GetString().Should().Be("rsi14");
        diags[2].GetProperty("timeframe").GetString().Should().Be("1h");
        diags[2].GetProperty("indicator").GetString().Should().Be("ema200");
    }

    // ─── Эталонный тест: полный JSON-фрагмент с null-индикаторами и diagnostics

    [Fact]
    public async Task LlmPayload_GoldenJson_Contains_Null_Indicators_And_Diagnostics()
    {
        // rsi14 = null (unavailable), ema200 использует fallback, atr14 = null.
        var snapshot = BuildSnapshotWithDiagnostics(
            [
                new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "ema200", Reason = "PartialWindow",    IsFallback = true,  Message = "15m.ema200 calculated using fallback: PartialWindow." },
                new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "rsi14",  Reason = "InsufficientData", IsFallback = false, Message = "15m.rsi14 unavailable: InsufficientData." },
                new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "atr14",  Reason = "InsufficientData", IsFallback = false, Message = "15m.atr14 unavailable: InsufficientData." },
            ],
            overrideRsi14: null, overrideRsiOverbought: false, overrideRsiOversold: false,
            overrideAtr14: null, overrideAtrIsReliable: false);

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var m15 = json.RootElement.GetProperty("m15");

        // rsi14 должен быть null, а не 0.
        m15.GetProperty("rsi14").ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable RSI must be null in JSON, not 0");

        // atr14 должен быть null, а не 0.
        m15.GetProperty("atr14").ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable ATR must be null in JSON, not 0");

        // ema200 имеет числовое значение (fallback).
        m15.GetProperty("ema200").ValueKind.Should().Be(JsonValueKind.Number,
            because: "EMA200 fallback has a numeric value");

        // Ложные oversold/overbought отсутствуют.
        m15.GetProperty("rsiOversold").GetBoolean().Should().BeFalse();
        m15.GetProperty("rsiOverbought").GetBoolean().Should().BeFalse();

        // indicatorDiagnostics содержит 3 записи.
        var diags = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray().ToList();
        diags.Should().HaveCount(3);
        diags.Should().Contain(d => d.GetProperty("indicator").GetString() == "rsi14"
                                 && d.GetProperty("isFallback").GetBoolean() == false);
        diags.Should().Contain(d => d.GetProperty("indicator").GetString() == "ema200"
                                 && d.GetProperty("isFallback").GetBoolean() == true);
        diags.Should().Contain(d => d.GetProperty("indicator").GetString() == "atr14"
                                 && d.GetProperty("isFallback").GetBoolean() == false);
    }

    // ─── Эталонный тест: недоступный VolumeRatio сериализуется как null + diagnostic

    [Fact]
    public async Task LlmPayload_Sets_VolumeRatio_To_Null_And_Adds_Diagnostic_When_Unavailable()
    {
        // VolumeRatio = null (например, VolumeSma20 == 0 → деление на ноль → InvalidInput).
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        snapshot = snapshot with
        {
            M15 = snapshot.M15 with { VolumeRatio = null, VolumeRatioIsReliable = false },
            H1 = snapshot.H1 with { VolumeRatio = null, VolumeRatioIsReliable = false },
            H4 = snapshot.H4 with { VolumeRatio = null, VolumeRatioIsReliable = false },
            D1 = snapshot.D1 with { VolumeRatio = null, VolumeRatioIsReliable = false },
            IndicatorDiagnostics =
            [
                new IndicatorDiagnosticSnapshot
                {
                    Timeframe  = "15m",
                    Indicator  = "volumeRatio",
                    Reason     = "InvalidInput",
                    IsFallback = false,
                    Message    = "15m.volumeRatio unavailable: InvalidInput.",
                },
            ],
        };

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // volumeRatio должен быть null, а не 0.
        var volumeRatio = json.RootElement.GetProperty("m15").GetProperty("volumeRatio");
        volumeRatio.ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable VolumeRatio must serialize as null, not 0");

        // Diagnostic присутствует с корректной причиной.
        var diag = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "volumeRatio");
        diag.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: "volumeRatio diagnostic must appear in indicatorDiagnostics");
        diag.GetProperty("reason").GetString().Should().Be("InvalidInput");
        diag.GetProperty("isFallback").GetBoolean().Should().BeFalse();
    }

    // ─── Эталонный тест: уровень не найден → плоское support1 = null в JSON ───

    [Fact]
    public async Task LlmPayload_Sets_Support1_To_Null_In_Json_When_Level_Not_Found()
    {
        // Плоское поле support1 при отсутствии уровня должно сериализоваться как null, а не как 0.
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        snapshot = snapshot with
        {
            M15 = snapshot.M15 with
            {
                Support1 = null,
                Support2 = null,
                Resistance1 = null,
                Resistance2 = null,
                DistanceToSupport1Pct = null,
                DistanceToResistance1Pct = null,
            },
        };

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var m15 = json.RootElement.GetProperty("m15");

        // Плоские поля уровней должны быть null.
        m15.GetProperty("support1").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent support1 level must be null in JSON, not 0");
        m15.GetProperty("support2").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent support2 level must be null in JSON, not 0");
        m15.GetProperty("resistance1").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent resistance1 level must be null in JSON, not 0");
        m15.GetProperty("resistance2").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent resistance2 level must be null in JSON, not 0");

        // Поля Meta должны полностью отсутствовать (JsonIgnore(WhenWritingNull)).
        m15.TryGetProperty("support1Meta", out _).Should().BeFalse(
            because: "absent level must not produce a meta object in JSON");
    }

    // ─── Эталонный тест: найденный уровень → JSON meta содержит все четыре поля

    [Fact]
    public async Task LlmPayload_Level_Meta_Contains_All_Fields_In_Json_When_Level_Found()
    {
        // Проверить, что при обнаружении уровня полный объект meta (price/strength//clusterVolume)
        // присутствует в исходном JSON wire format, а не только в типизированном C#-объекте.
        var snapshot = ApiSnapshotTestData.CreateSnapshot();

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var meta = json.RootElement.GetProperty("m15").GetProperty("support1Meta");

        // price — числовое значение, совпадающее с полем support1.
        meta.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Number,
            because: "support1Meta.price must be a number in JSON");
        meta.GetProperty("price").GetDecimal().Should().Be(
            json.RootElement.GetProperty("m15").GetProperty("support1").GetDecimal(),
            because: "meta.price must match flat support1 field");

        // strength — числовое значение в [0, 1].
        meta.GetProperty("strength").ValueKind.Should().Be(JsonValueKind.Number,
            because: "support1Meta.strength must be a number in JSON");
        meta.GetProperty("strength").GetDecimal().Should().BeInRange(0m, 1m);

        //  — строка volume-profile.
        meta.GetProperty("source").GetString().Should().Be("volume-profile",
            because: "only SimplifiedVolumeProfile detector is used in V1");

        // clusterVolume — числовое значение, > 0.
        meta.GetProperty("clusterVolume").ValueKind.Should().Be(JsonValueKind.Number,
            because: "support1Meta.clusterVolume must be serialized as a number in JSON");
        meta.GetProperty("clusterVolume").GetDecimal().Should().BePositive(
            because: "clusterVolume of a detected level must be > 0");
    }

    // ─── Вспомогательные методы ────────────────────────────────────────────────

    private static Mock<IMarketSnapshotService> MockService(MarketSnapshot snapshot)
    {
        var mock = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        mock.Setup(x => x.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        return mock;
    }

    /// <summary>
    /// Строит снимок с указанными диагностическими данными и необязательными переопределениями значений индикаторов.
    /// </summary>
    private static MarketSnapshot BuildSnapshotWithDiagnostics(
        IReadOnlyList<IndicatorDiagnosticSnapshot> diagnostics,
        decimal? overrideRsi14 = 55m,
        bool overrideRsiOverbought = false,
        bool overrideRsiOversold = false,
        decimal? overrideAtr14 = 180m,
        bool overrideAtrIsReliable = true)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: overrideRsi14,
            overrideRsiOverbought: overrideRsiOverbought,
            overrideRsiOversold: overrideRsiOversold);

        // При необходимости применить переопределение ATR ко всем таймфреймам.
        if (!overrideAtrIsReliable || overrideAtr14 != 180m)
        {
            snapshot = snapshot with
            {
                M15 = snapshot.M15 with { Atr14 = overrideAtr14, AtrIsReliable = overrideAtrIsReliable },
                H1 = snapshot.H1 with { Atr14 = overrideAtr14, AtrIsReliable = overrideAtrIsReliable },
                H4 = snapshot.H4 with { Atr14 = overrideAtr14, AtrIsReliable = overrideAtrIsReliable },
                D1 = snapshot.D1 with { Atr14 = overrideAtr14, AtrIsReliable = overrideAtrIsReliable },
            };
        }

        return snapshot with { IndicatorDiagnostics = diagnostics };
    }
}
