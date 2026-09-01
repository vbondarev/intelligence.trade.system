using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Проверяет, что <c>trendCode</c> и <c>trend</c> в LLM payload всегда консистентны
/// и соответствуют контракту: Unknown=0, Bullish=1, Bearish=2, Sideways=3.
/// </summary>
public sealed class LlmTrendCodeMappingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LlmTrendCodeMappingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── trendCode contract: value per enum ─────────────────────────────────

    [Theory]
    [InlineData(MarketTrend.Unknown, 0, "Unknown")]
    [InlineData(MarketTrend.Bullish, 1, "Bullish")]
    [InlineData(MarketTrend.Bearish, 2, "Bearish")]
    [InlineData(MarketTrend.Sideways, 3, "Sideways")]
    public async Task TrendCode_And_TrendLabel_Match_Contract_For_All_Enum_Values(
        MarketTrend trend, int expectedCode, string expectedLabel)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(trend);
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync(
            "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result.Should().NotBeNull();

        // All four timeframes must carry the same code and label.
        foreach (var tf in new[] { result!.M15, result.H1, result.H4, result.D1 })
        {
            tf.Should().NotBeNull();
            tf!.TrendCode.Should().Be(expectedCode,
                because: $"trendCode for {tf.Timeframe} with trend {trend} must be {expectedCode}");
            tf.Trend.Should().Be(expectedLabel,
                because: $"trend label for {tf.Timeframe} with trend {trend} must be '{expectedLabel}'");
        }
    }

    // ─── trend / trendCode consistency ──────────────────────────────────────

    [Theory]
    [InlineData(MarketTrend.Unknown)]
    [InlineData(MarketTrend.Bullish)]
    [InlineData(MarketTrend.Bearish)]
    [InlineData(MarketTrend.Sideways)]
    public async Task TrendCode_Is_Always_Consistent_With_Trend_Label(MarketTrend trend)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(trend);
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync(
            "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        foreach (var timeframeProp in new[] { "m15", "h1", "h4", "d1" })
        {
            var tf = json.RootElement.GetProperty(timeframeProp);
            var label = tf.GetProperty("trend").GetString()!;
            var code = tf.GetProperty("trendCode").GetInt32();

            // Roundtrip: parse the label back to enum and cast to int — must equal trendCode.
            var parsedTrend = Enum.Parse<MarketTrend>(label);
            ((int)parsedTrend).Should().Be(code,
                because: $"{timeframeProp}: (int)Enum.Parse(\"{label}\") should equal trendCode {code}");
        }
    }

    // ─── regression: Sideways must not be 0 ─────────────────────────────────

    [Fact]
    public async Task TrendCode_For_Sideways_Is_3_Not_0()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Sideways);
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync(
            "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result.Should().NotBeNull();
        result!.H1.TrendCode.Should().Be(3, because: "Sideways maps to 3, not 0");
        result.H1.Trend.Should().Be("Sideways");
    }

    // ─── regression: Bearish must not be -1 ─────────────────────────────────

    [Fact]
    public async Task TrendCode_For_Bearish_Is_2_Not_Minus1()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bearish);
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync(
            "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result.Should().NotBeNull();
        result!.H1.TrendCode.Should().Be(2, because: "Bearish maps to 2, not -1");
        result.H1.Trend.Should().Be("Bearish");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
}
