using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class LlmPayloadEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LlmPayloadEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── 200 OK ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Returns_Ok_With_Valid_Payload_For_Default_Mode()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be("1.0");
        result.Symbol.Should().Be("BTCUSDT");
        result.Exchange.Should().Be("Bybit");
        result.AnalysisContext.AnalysisMode.Should().Be("Intraday");
        result.AnalysisContext.PrimaryTimeframes.Should().Equal("15m", "1h", "4h");
        result.SnapshotHealth.Should().NotBeNull();
        result.Tags.Should().ContainInOrder("trend", "momentum");
        result.Tags.Should().Contain("no-clean-entry");
        result.Tags.Should().Contain("trend-confirmed-entry-filtered");

        service.Verify(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LlmPayload_Returns_Correct_SchemaVersion()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
    }

    [Fact]
    public async Task LlmPayload_Returns_SnapshotHealth_Section()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var health = json.RootElement.GetProperty("snapshotHealth");

        health.TryGetProperty("isFresh", out _).Should().BeTrue();
        health.TryGetProperty("isPartial", out _).Should().BeTrue();
        health.TryGetProperty("warnings", out _).Should().BeTrue();
        health.GetProperty("isPartial").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task LlmPayload_Timeframes_Contain_TrendCode_And_TrendStrengthLabel_And_Summary()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        foreach (var tf in new[] { "m15", "h1", "h4", "d1" })
        {
            var tfEl = json.RootElement.GetProperty(tf);
            tfEl.TryGetProperty("trendCode", out _).Should().BeTrue($"timeframe {tf} должен содержать trendCode");
            tfEl.TryGetProperty("trendStrengthLabel", out _).Should().BeTrue($"timeframe {tf} должен содержать trendStrengthLabel");
            var summary = tfEl.GetProperty("summary");
            summary.TryGetProperty("bias", out _).Should().BeTrue();
            summary.TryGetProperty("isTrendConfirmed", out _).Should().BeTrue();
            summary.TryGetProperty("momentumState", out _).Should().BeTrue();
            summary.TryGetProperty("entryQuality", out _).Should().BeTrue();
            summary.TryGetProperty("riskFlags", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task LlmPayload_OrderBook_Does_Not_Contain_TopBids_Or_TopAsks()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderBook = json.RootElement.GetProperty("orderBook");

        orderBook.TryGetProperty("topBids", out _).Should().BeFalse("topBids должны быть исключены из LLM payload");
        orderBook.TryGetProperty("topAsks", out _).Should().BeFalse("topAsks должны быть исключены из LLM payload");
    }

    [Fact]
    public async Task LlmPayload_OrderBook_Contains_Computed_Spread_And_Labels()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderBook = json.RootElement.GetProperty("orderBook");

        orderBook.GetProperty("spreadAbs").GetDecimal().Should().Be(10m); // 65005 - 64995
        orderBook.TryGetProperty("spreadPct", out _).Should().BeTrue();
        orderBook.TryGetProperty("pressureLabel", out _).Should().BeTrue();
        orderBook.TryGetProperty("liquiditySkewLabel", out _).Should().BeTrue();
    }

    [Fact]
    public async Task LlmPayload_With_Mode_Swing_Sets_Correct_PrimaryTimeframes()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=Swing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result!.AnalysisContext.AnalysisMode.Should().Be("Swing");
        result.AnalysisContext.PrimaryTimeframes.Should().Equal("1h", "4h", "1d");
    }

    [Fact]
    public async Task LlmPayload_Trims_Symbol_Before_Calling_Service()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/%20BTCUSDT%20/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        service.Verify(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── trendCode mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Timeframe_TrendCode_Is_1_For_Bullish_Trend()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(); // all timeframes are Bullish
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("h1").GetProperty("trendCode").GetInt32().Should().Be(1);
    }

    // ─── trendStrengthLabel ─────────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_TrendStrengthLabel_Is_Weak_When_Score_Below_0_5()
    {
        // TrendStrengthScore = 0.4 in test data
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("h1").GetProperty("trendStrengthLabel").GetString().Should().Be("Weak");
    }

    // ─── 400 Bad Request ────────────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_When_Exchange_Is_Missing()
    {
        var service = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?category=Linear");

        await ProblemDetailsAssertions.AssertProblemAsync(response, HttpStatusCode.BadRequest, "Request validation failed.", "exchange");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_When_Category_Is_Missing()
    {
        var service = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit");

        await ProblemDetailsAssertions.AssertProblemAsync(response, HttpStatusCode.BadRequest, "Request validation failed.", "category");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_With_AllowedValues_When_Mode_Is_Invalid()
    {
        var service = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=scalping");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.BadRequest);
        root.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        root.GetProperty("errors").ToString().Should().Contain("mode");

        service.VerifyNoOtherCalls();
    }

    // ─── 503 Service Unavailable ────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Returns_ServiceUnavailable_When_Service_Throws_InvalidOperationException()
    {
        var service = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        service
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ticker temporarily unavailable."));

        using var client = _factory.CreateClientWithMarketSnapshotService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "LLM payload analysis is temporarily unavailable.",
            "temporarily unavailable");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

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
