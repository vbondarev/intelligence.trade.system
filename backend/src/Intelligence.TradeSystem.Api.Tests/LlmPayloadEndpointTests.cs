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

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
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
        result.Tags.Should().Equal("trend", "momentum");

        service.Verify(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LlmPayload_Returns_Correct_SchemaVersion()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
    }

    [Fact]
    public async Task LlmPayload_Returns_SnapshotHealth_Section()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
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

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
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

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
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

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
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

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=Swing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result!.AnalysisContext.AnalysisMode.Should().Be("Swing");
        result.AnalysisContext.PrimaryTimeframes.Should().Equal("1h", "4h", "1d");
    }

    [Fact]
    public async Task LlmPayload_Without_IncludePortfolio_Portfolio_Key_Is_Absent_From_Json()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("portfolio", out _).Should().BeFalse(
            because: "portfolio must not appear in JSON when includePortfolio is false");
    }

    [Fact]
    public async Task LlmPayload_With_IncludePortfolio_True_Returns_Portfolio()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&includePortfolio=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result!.Portfolio.Should().NotBeNull();
        result.Portfolio!.OpenPositions.Should().ContainSingle(p => p.Symbol == "BTCUSDT" && p.Side == "Long");
        result.AnalysisContext.UsesPortfolioContext.Should().BeTrue();
    }

    [Fact]
    public async Task LlmPayload_Trims_Symbol_Before_Calling_Service()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/%20BTCUSDT%20/llm-payload?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        service.Verify(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LlmPayload_With_IncludePortfolio_True_Returns_IsAvailable_True()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&includePortfolio=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("portfolio", out var portfolioEl).Should().BeTrue(
            because: "portfolio key must be present when includePortfolio=true");
        portfolioEl.GetProperty("isAvailable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LlmPayload_AggregatedContext_Key_Is_Always_Absent_From_Json()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("aggregatedContext", out _).Should().BeFalse(
            because: "aggregatedContext is not supported in V1 and must not appear in payload");
    }

    // ─── trendCode mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Timeframe_TrendCode_Is_1_For_Bullish_Trend()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(); // all timeframes are Bullish
        var service = MockService(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
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

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("h1").GetProperty("trendStrengthLabel").GetString().Should().Be("Weak");
    }

    // ─── 400 Bad Request ────────────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_When_Exchange_Is_Missing()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?category=Linear");

        await ProblemDetailsAssertions.AssertProblemAsync(response, HttpStatusCode.BadRequest, "Request validation failed.", "exchange");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_When_Category_Is_Missing()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit");

        await ProblemDetailsAssertions.AssertProblemAsync(response, HttpStatusCode.BadRequest, "Request validation failed.", "category");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_With_AllowedValues_When_Mode_Is_Invalid()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=scalping");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = json.RootElement.GetProperty("detail").GetString();
        detail.Should().Contain("mode");
        detail.Should().Contain("Intraday");
        detail.Should().Contain("Swing");
        detail.Should().Contain("Portfolio");

        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LlmPayload_Returns_BadRequest_When_IncludeAggregatedContext_Is_True()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&includeAggregatedContext=true");

        await ProblemDetailsAssertions.AssertProblemAsync(response, HttpStatusCode.BadRequest, "Request validation failed.", "not supported");
        service.VerifyNoOtherCalls();
    }

    // ─── 503 Service Unavailable ────────────────────────────────────────────

    [Fact]
    public async Task LlmPayload_Returns_ServiceUnavailable_When_Service_Throws_InvalidOperationException()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        service
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ticker temporarily unavailable."));

        using var client = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear");

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "LLM payload analysis is temporarily unavailable.",
            "temporarily unavailable");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Mock<IMarketAnalysisService> MockService(
        Intelligence.TradeSystem.Domain.Snapshots.MarketAnalysisSnapshot snapshot)
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
}
