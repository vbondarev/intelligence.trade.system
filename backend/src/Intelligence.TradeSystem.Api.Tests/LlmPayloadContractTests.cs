using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class LlmPayloadContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LlmPayloadContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LlmPayload_V1_Uses_Exact_Public_Json_Shape_And_Excludes_Portfolio_Data()
    {
        using var client = CreateClient(ApiSnapshotTestData.CreateSnapshot());
        using var response = await client.GetAsync(LlmPayloadPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        JsonContractAssertions.AssertExactPropertyNames(root,
            "schemaVersion", "exchange", "symbol", "category", "capturedAtUtc", "analysisContext",
            "snapshotHealth", "price", "derivatives", "orderBook", "tradeFlow", "m15", "h1", "h4",
            "d1", "sentiment", "tags", "indicatorDiagnostics");

        root.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        root.GetProperty("exchange").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("symbol").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("category").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("capturedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("tags").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("indicatorDiagnostics").ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var privateProperty in new[] { "portfolio", "account", "positions", "openPositions" })
            root.TryGetProperty(privateProperty, out _).Should().BeFalse();

        AssertAnalysisContext(root.GetProperty("analysisContext"));
        AssertSnapshotHealth(root.GetProperty("snapshotHealth"));
        AssertPrice(root.GetProperty("price"));
        AssertDerivatives(root.GetProperty("derivatives"));
        AssertOrderBook(root.GetProperty("orderBook"));
        AssertTradeFlow(root.GetProperty("tradeFlow"));
        AssertSentiment(root.GetProperty("sentiment"));

        foreach (var timeframe in new[] { "m15", "h1", "h4", "d1" })
            AssertTimeframe(root.GetProperty(timeframe));
    }

    [Fact]
    public async Task LlmPayload_V1_Serializes_IndicatorDiagnostic_When_Diagnostics_Are_Present()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            IndicatorDiagnostics =
            [
                new IndicatorDiagnosticSnapshot
                {
                    Timeframe = "15m",
                    Indicator = "rsi14",
                    Reason = "InsufficientData",
                    IsFallback = false,
                    Message = "15m.rsi14 unavailable: InsufficientData.",
                },
            ],
        };

        using var client = CreateClient(snapshot);
        using var response = await client.GetAsync(LlmPayloadPath);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var diagnostic = json.RootElement.GetProperty("indicatorDiagnostics").EnumerateArray().Single();

        JsonContractAssertions.AssertExactPropertyNames(
            diagnostic, "timeframe", "indicator", "reason", "isFallback", "message");
        diagnostic.GetProperty("timeframe").ValueKind.Should().Be(JsonValueKind.String);
        diagnostic.GetProperty("indicator").ValueKind.Should().Be(JsonValueKind.String);
        diagnostic.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.String);
        diagnostic.GetProperty("isFallback").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        diagnostic.GetProperty("message").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Theory]
    [InlineData("Intraday")]
    [InlineData("Swing")]
    [InlineData("Portfolio")]
    public async Task LlmPayload_V1_Always_Includes_All_Timeframes_For_Each_Analysis_Mode(string mode)
    {
        using var client = CreateClient(ApiSnapshotTestData.CreateSnapshot());
        using var response = await client.GetAsync($"{LlmPayloadPath}&mode={mode}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var timeframe in new[] { "m15", "h1", "h4", "d1" })
            json.RootElement.GetProperty(timeframe).ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task LlmPayload_Portfolio_Mode_Uses_Portfolio_Primary_Timeframes()
    {
        using var client = CreateClient(ApiSnapshotTestData.CreateSnapshot());
        using var response = await client.GetAsync($"{LlmPayloadPath}&mode=Portfolio");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var context = json.RootElement.GetProperty("analysisContext");

        context.GetProperty("analysisMode").GetString().Should().Be("Portfolio");
        context.GetProperty("primaryTimeframes").EnumerateArray()
            .Select(timeframe => timeframe.GetString())
            .Should().Equal("4h", "1d");
    }

    private HttpClient CreateClient(MarketAnalysisSnapshot snapshot)
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        service.Setup(x => x.BuildSnapshotAsync(
                ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        return _factory.CreateClientWithMarketAnalysisService(service.Object);
    }

    private static void AssertAnalysisContext(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "analysisMode", "primaryTimeframes");
        element.GetProperty("analysisMode").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("primaryTimeframes").ValueKind.Should().Be(JsonValueKind.Array);
    }

    private static void AssertSnapshotHealth(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(
            element, "isFresh", "isPartial", "warnings", "missingSections", "sectionAgesMs");
        element.GetProperty("isFresh").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        element.GetProperty("isPartial").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        element.GetProperty("warnings").ValueKind.Should().Be(JsonValueKind.Array);
        JsonContractAssertions.AssertValueKind(element.GetProperty("missingSections"), JsonValueKind.Array, JsonValueKind.Null);
        JsonContractAssertions.AssertValueKind(element.GetProperty("sectionAgesMs"), JsonValueKind.Object, JsonValueKind.Null);
    }

    private static void AssertPrice(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element,
            "lastPrice", "markPrice", "indexPrice", "spreadAbs", "spreadPct", "price24hChangePct",
            "high24h", "low24h", "volume24h");

        AssertNumberProperties(element, "lastPrice", "markPrice", "indexPrice", "spreadAbs", "spreadPct",
            "price24hChangePct", "high24h", "low24h", "volume24h");
    }

    private static void AssertDerivatives(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element,
            "fundingRate", "fundingRateAvg24h", "nextFundingTimeUtc", "openInterest", "openInterestValue",
            "openInterestChange1hPct", "openInterestChange4hPct", "longRatio", "shortRatio", "premiumVsIndexPct");

        AssertNumberProperties(element, "fundingRate", "fundingRateAvg24h", "openInterest", "openInterestValue",
            "openInterestChange1hPct", "openInterestChange4hPct", "longRatio", "shortRatio", "premiumVsIndexPct");
        JsonContractAssertions.AssertValueKind(element.GetProperty("nextFundingTimeUtc"), JsonValueKind.String, JsonValueKind.Null);
    }

    private static void AssertOrderBook(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element,
            "capturedAtUtc", "bestBidPrice", "bestAskPrice", "spreadAbs", "spreadPct", "totalBidVolumeTop5",
            "totalAskVolumeTop5", "totalBidVolumeTop10", "totalAskVolumeTop10", "totalBidVolumeTop20",
            "totalAskVolumeTop20", "imbalanceTop5", "imbalanceTop10", "imbalanceTop20", "bidWalls", "askWalls",
            "pressureLabel", "liquiditySkewLabel");

        element.GetProperty("capturedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(element, "bestBidPrice", "bestAskPrice", "spreadAbs", "spreadPct", "totalBidVolumeTop5",
            "totalAskVolumeTop5", "totalBidVolumeTop10", "totalAskVolumeTop10", "totalBidVolumeTop20",
            "totalAskVolumeTop20", "imbalanceTop5", "imbalanceTop10", "imbalanceTop20");
        element.GetProperty("bidWalls").ValueKind.Should().Be(JsonValueKind.Array);
        element.GetProperty("askWalls").ValueKind.Should().Be(JsonValueKind.Array);
        element.GetProperty("pressureLabel").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("liquiditySkewLabel").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static void AssertTradeFlow(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element,
            "windowStartUtc", "windowEndUtc", "buyVolume", "sellVolume", "deltaVolume", "deltaPct", "buyTrades",
            "sellTrades", "avgTradeSize", "maxTradeSize", "hasAggressiveBuyPressure", "hasAggressiveSellPressure");

        element.GetProperty("windowStartUtc").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("windowEndUtc").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(element, "buyVolume", "sellVolume", "deltaVolume", "deltaPct", "buyTrades", "sellTrades",
            "avgTradeSize", "maxTradeSize");
        element.GetProperty("hasAggressiveBuyPressure").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        element.GetProperty("hasAggressiveSellPressure").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    private static void AssertTimeframe(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element,
            "timeframe", "trend", "trendCode", "trendStrengthScore", "trendStrengthLabel", "ema20", "ema50", "ema200",
            "rsi14", "rsi14IsReliable", "atr14", "volumeRatio", "support1", "support2", "resistance1", "resistance2",
            "distanceToSupport1Pct", "distanceToResistance1Pct", "support1Meta", "support2Meta", "resistance1Meta",
            "resistance2Meta", "isAboveEma20", "isAboveEma50", "isAboveEma200", "emaBullishAlignment",
            "emaBearishAlignment", "rsiOverbought", "rsiOversold", "summary");

        element.GetProperty("timeframe").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("trend").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("trendCode").ValueKind.Should().Be(JsonValueKind.Number);
        element.GetProperty("trendStrengthLabel").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(element, "trendStrengthScore", "ema20", "ema50", "ema200", "rsi14", "atr14", "volumeRatio",
            "support1", "support2", "resistance1", "resistance2", "distanceToSupport1Pct", "distanceToResistance1Pct");

        foreach (var flag in new[] { "rsi14IsReliable", "isAboveEma20", "isAboveEma50", "isAboveEma200",
                     "emaBullishAlignment", "emaBearishAlignment", "rsiOverbought", "rsiOversold" })
            element.GetProperty(flag).ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);

        AssertTimeframeSummary(element.GetProperty("summary"));
    }

    private static void AssertTimeframeSummary(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(
            element, "bias", "isTrendConfirmed", "momentumState", "entryQuality", "riskFlags");
        element.GetProperty("bias").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("isTrendConfirmed").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        element.GetProperty("momentumState").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("entryQuality").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("riskFlags").ValueKind.Should().Be(JsonValueKind.Array);
    }

    private static void AssertSentiment(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(
            element, "longShortBiasScore", "fundingBiasScore", "orderBookPressureScore", "tradeFlowPressureScore", "marketRegime");
        AssertNumberProperties(element, "longShortBiasScore", "fundingBiasScore", "orderBookPressureScore", "tradeFlowPressureScore");
        element.GetProperty("marketRegime").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static void AssertNumberProperties(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            JsonContractAssertions.AssertValueKind(element.GetProperty(propertyName), JsonValueKind.Number, JsonValueKind.Null);
    }

    private const string LlmPayloadPath = "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear";
}
