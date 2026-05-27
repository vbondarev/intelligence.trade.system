using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Golden / integration tests для LLM payload с unavailable/fallback-индикаторами.
/// Проверяют:
/// - nullable indicator fields сериализуются как <c>null</c>, не как <c>0</c>;
/// - <c>indicatorDiagnostics</c> появляются при unavailable/fallback-индикаторах;
/// - при полном наборе данных <c>indicatorDiagnostics</c> пустой;
/// - JSON-контракт стабилен.
/// </summary>
public sealed class IndicatorDiagnosticsGoldenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IndicatorDiagnosticsGoldenTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── Golden: RSI unavailable serializes as null, not 0 ───────────────────

    [Fact]
    public async Task LlmPayload_Sets_Rsi14_To_Null_And_Adds_Diagnostic_When_Insufficient_Candles()
    {
        // Snapshot with RSI unavailable (Rsi14 = null, Rsi14IsReliable = false).
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: null, overrideRsiOverbought: false, overrideRsiOversold: false);

        // Inject a diagnostic explaining the null.
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
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // rsi14 must be null, not 0.
        var rsi = json.RootElement.GetProperty("m15").GetProperty("rsi14");
        rsi.ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable RSI must serialize as null, not 0");

        // rsiOversold/rsiOverbought must be false.
        json.RootElement.GetProperty("m15").GetProperty("rsiOversold").GetBoolean()
            .Should().BeFalse();
        json.RootElement.GetProperty("m15").GetProperty("rsiOverbought").GetBoolean()
            .Should().BeFalse();

        // indicatorDiagnostics contains the rsi14 diagnostic.
        var diags = json.RootElement.GetProperty("indicatorDiagnostics");
        diags.ValueKind.Should().Be(JsonValueKind.Array);
        var rsiDiag = diags.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "rsi14");
        rsiDiag.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: "rsi14 diagnostic must appear in indicatorDiagnostics");
        rsiDiag.GetProperty("reason").GetString().Should().Be("InsufficientData");
        rsiDiag.GetProperty("isFallback").GetBoolean().Should().BeFalse();
    }

    // ─── Golden: ATR unavailable serializes as null ───────────────────────────

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
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var atr = json.RootElement.GetProperty("h1").GetProperty("atr14");
        atr.ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable ATR must serialize as null, not 0");

        // Diagnostic present.
        var diags = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "atr14");
        diags.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        diags.GetProperty("reason").GetString().Should().Be("InsufficientData");
    }

    // ─── Golden: EMA200 partial window adds fallback diagnostic ───────────────

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
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // ema200 has a numeric value (fallback, not null).
        var ema200 = json.RootElement.GetProperty("m15").GetProperty("ema200");
        ema200.ValueKind.Should().Be(JsonValueKind.Number,
            because: "EMA200 fallback should produce a numeric value, not null");

        // Fallback diagnostic present.
        var diag = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "ema200");
        diag.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        diag.GetProperty("reason").GetString().Should().Be("PartialWindow");
        diag.GetProperty("isFallback").GetBoolean().Should().BeTrue();
    }

    // ─── Golden: indicatorDiagnostics empty with full data ────────────────────

    [Fact]
    public async Task LlmPayload_Has_Empty_IndicatorDiagnostics_When_All_Indicators_Available()
    {
        // All indicators available → no diagnostics.
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        // IndicatorDiagnostics defaults to [] in MarketAnalysisSnapshot.

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var diags = json.RootElement.GetProperty("indicatorDiagnostics");
        diags.ValueKind.Should().Be(JsonValueKind.Array);
        diags.GetArrayLength().Should().Be(0,
            because: "no diagnostics expected when all indicators are fully available");
    }

    // ─── Golden: stable order of indicatorDiagnostics in JSON ─────────────────

    [Fact]
    public async Task LlmPayload_IndicatorDiagnostics_Are_In_Stable_Order_In_Json()
    {
        var snapshot = BuildSnapshotWithDiagnostics([
            new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "ema200", Reason = "PartialWindow", IsFallback = true,  Message = "15m.ema200 calculated using fallback: PartialWindow." },
            new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "rsi14",  Reason = "InsufficientData", IsFallback = false, Message = "15m.rsi14 unavailable: InsufficientData." },
            new IndicatorDiagnosticSnapshot { Timeframe = "1h",  Indicator = "ema200", Reason = "PartialWindow", IsFallback = true,  Message = "1h.ema200 calculated using fallback: PartialWindow." },
        ]);

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var diags = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray().ToList();

        diags.Should().HaveCount(3);

        // Stable order: 15m ema200 → 15m rsi14 → 1h ema200.
        diags[0].GetProperty("timeframe").GetString().Should().Be("15m");
        diags[0].GetProperty("indicator").GetString().Should().Be("ema200");
        diags[1].GetProperty("timeframe").GetString().Should().Be("15m");
        diags[1].GetProperty("indicator").GetString().Should().Be("rsi14");
        diags[2].GetProperty("timeframe").GetString().Should().Be("1h");
        diags[2].GetProperty("indicator").GetString().Should().Be("ema200");
    }

    // ─── Golden: complete JSON fragment with null indicators and diagnostics ──

    [Fact]
    public async Task LlmPayload_GoldenJson_Contains_Null_Indicators_And_Diagnostics()
    {
        // rsi14 = null (unavailable) + ema200 = fallback + atr14 = null.
        var snapshot = BuildSnapshotWithDiagnostics(
            [
                new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "ema200", Reason = "PartialWindow",    IsFallback = true,  Message = "15m.ema200 calculated using fallback: PartialWindow." },
                new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "rsi14",  Reason = "InsufficientData", IsFallback = false, Message = "15m.rsi14 unavailable: InsufficientData." },
                new IndicatorDiagnosticSnapshot { Timeframe = "15m", Indicator = "atr14",  Reason = "InsufficientData", IsFallback = false, Message = "15m.atr14 unavailable: InsufficientData." },
            ],
            overrideRsi14: null, overrideRsiOverbought: false, overrideRsiOversold: false,
            overrideAtr14: null, overrideAtrIsReliable: false);

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var m15 = json.RootElement.GetProperty("m15");

        // rsi14 must be null, not 0.
        m15.GetProperty("rsi14").ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable RSI must be null in JSON, not 0");

        // atr14 must be null, not 0.
        m15.GetProperty("atr14").ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable ATR must be null in JSON, not 0");

        // ema200 has a number (fallback).
        m15.GetProperty("ema200").ValueKind.Should().Be(JsonValueKind.Number,
            because: "EMA200 fallback has a numeric value");

        // No false oversold/overbought.
        m15.GetProperty("rsiOversold").GetBoolean().Should().BeFalse();
        m15.GetProperty("rsiOverbought").GetBoolean().Should().BeFalse();

        // indicatorDiagnostics has 3 entries.
        var diags = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray().ToList();
        diags.Should().HaveCount(3);
        diags.Should().Contain(d => d.GetProperty("indicator").GetString() == "rsi14"
                                 && d.GetProperty("isFallback").GetBoolean() == false);
        diags.Should().Contain(d => d.GetProperty("indicator").GetString() == "ema200"
                                 && d.GetProperty("isFallback").GetBoolean() == true);
        diags.Should().Contain(d => d.GetProperty("indicator").GetString() == "atr14"
                                 && d.GetProperty("isFallback").GetBoolean() == false);
    }

    // ─── Golden: VolumeRatio unavailable serializes as null + diagnostic ────────

    [Fact]
    public async Task LlmPayload_Sets_VolumeRatio_To_Null_And_Adds_Diagnostic_When_Unavailable()
    {
        // VolumeRatio = null (e.g. VolumeSma20 == 0 → division by zero → InvalidInput).
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
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // volumeRatio must be null, not 0.
        var volumeRatio = json.RootElement.GetProperty("m15").GetProperty("volumeRatio");
        volumeRatio.ValueKind.Should().Be(JsonValueKind.Null,
            because: "unavailable VolumeRatio must serialize as null, not 0");

        // Diagnostic present with correct reason.
        var diag = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("indicator").GetString() == "volumeRatio");
        diag.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: "volumeRatio diagnostic must appear in indicatorDiagnostics");
        diag.GetProperty("reason").GetString().Should().Be("InvalidInput");
        diag.GetProperty("isFallback").GetBoolean().Should().BeFalse();
    }

    // ─── Golden: level not found → flat support1 = null in JSON ──────────────

    [Fact]
    public async Task LlmPayload_Sets_Support1_To_Null_In_Json_When_Level_Not_Found()
    {
        // Flat support1 field must serialize as null when the level is absent — not as 0.
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
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var m15 = json.RootElement.GetProperty("m15");

        // Flat level fields must be null.
        m15.GetProperty("support1").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent support1 level must be null in JSON, not 0");
        m15.GetProperty("support2").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent support2 level must be null in JSON, not 0");
        m15.GetProperty("resistance1").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent resistance1 level must be null in JSON, not 0");
        m15.GetProperty("resistance2").ValueKind.Should().Be(JsonValueKind.Null,
            because: "absent resistance2 level must be null in JSON, not 0");

        // Meta fields must be absent entirely (JsonIgnore(WhenWritingNull)).
        m15.TryGetProperty("support1Meta", out _).Should().BeFalse(
            because: "absent level must not produce a meta object in JSON");
    }

    // ─── Golden: level found → JSON meta contains all four fields ────────────

    [Fact]
    public async Task LlmPayload_Level_Meta_Contains_All_Fields_In_Json_When_Level_Found()
    {
        // Verify that when a level is detected, the full meta object (price/strength/source/clusterVolume)
        // is present in the raw JSON wire format — not just in the typed C# object.
        var snapshot = ApiSnapshotTestData.CreateSnapshot();

        var service = MockService(snapshot);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var meta = json.RootElement.GetProperty("m15").GetProperty("support1Meta");

        // price — numeric, matches flat support1.
        meta.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Number,
            because: "support1Meta.price must be a number in JSON");
        meta.GetProperty("price").GetDecimal().Should().Be(
            json.RootElement.GetProperty("m15").GetProperty("support1").GetDecimal(),
            because: "meta.price must match flat support1 field");

        // strength — numeric, in [0, 1].
        meta.GetProperty("strength").ValueKind.Should().Be(JsonValueKind.Number,
            because: "support1Meta.strength must be a number in JSON");
        meta.GetProperty("strength").GetDecimal().Should().BeInRange(0m, 1m);

        // source — string, volume-profile.
        meta.GetProperty("source").GetString().Should().Be("volume-profile",
            because: "only SimplifiedVolumeProfile detector is used in V1");

        // clusterVolume — numeric, > 0.
        meta.GetProperty("clusterVolume").ValueKind.Should().Be(JsonValueKind.Number,
            because: "support1Meta.clusterVolume must be serialized as a number in JSON");
        meta.GetProperty("clusterVolume").GetDecimal().Should().BePositive(
            because: "clusterVolume of a detected level must be > 0");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Mock<IMarketAnalysisService> MockService(MarketAnalysisSnapshot snapshot)
    {
        var mock = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        mock.Setup(x => x.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        return mock;
    }

    /// <summary>
    /// Builds a snapshot with the given diagnostics and optional overrides for indicator values.
    /// </summary>
    private static MarketAnalysisSnapshot BuildSnapshotWithDiagnostics(
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

        // Apply ATR override to all timeframes if needed.
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
