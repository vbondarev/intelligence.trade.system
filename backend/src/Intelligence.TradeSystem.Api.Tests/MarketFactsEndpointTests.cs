using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Models.MarketFacts;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Endpoint-тесты для <c>GET /api/market-analysis/{symbol}/market-facts</c>.
/// Проверяют HTTP-контракт, payload-структуру, валидацию и обработку ошибок.
/// </summary>
public sealed class MarketFactsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MarketFactsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ===========================================================================
    // 200 OK — payload structure
    // ===========================================================================

    [Fact]
    public async Task MarketFacts_Returns_Ok_With_Valid_Payload()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service  = MockService(snapshot);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<MarketFactsPayload>();
        result.Should().NotBeNull();
        result.SchemaVersion.Should().Be("market-facts/v1");
        result.Source.Symbol.Should().Be("BTCUSDT");
        result.Source.Exchange.Should().Be("Bybit");

        service.Verify(x => x.BuildSnapshotAsync(
            ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarketFacts_Returns_Correct_SchemaVersion()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be("market-facts/v1");
    }

    [Fact]
    public async Task MarketFacts_Returns_DataQuality_Section()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var dq = json.RootElement.GetProperty("dataQuality");

        dq.TryGetProperty("status", out _).Should().BeTrue();
        dq.TryGetProperty("isFresh", out _).Should().BeTrue();
        dq.TryGetProperty("isPartial", out _).Should().BeTrue();
        dq.TryGetProperty("warnings", out _).Should().BeTrue();
        dq.GetProperty("isPartial").GetBoolean().Should().BeFalse();
        dq.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task MarketFacts_Returns_TradeFlow_Section()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tf = json.RootElement.GetProperty("tradeFlow");

        tf.TryGetProperty("direction", out _).Should().BeTrue();
        tf.TryGetProperty("label", out _).Should().BeTrue();
        tf.TryGetProperty("buyVolume", out _).Should().BeTrue();
        tf.TryGetProperty("sellVolume", out _).Should().BeTrue();
    }

    [Fact]
    public async Task MarketFacts_Returns_Timeframes_Section()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var timeframes = json.RootElement.GetProperty("timeframes");

        timeframes.TryGetProperty("15m", out _).Should().BeTrue("timeframes must contain '15m'");
        timeframes.TryGetProperty("1h", out _).Should().BeTrue("timeframes must contain '1h'");
        timeframes.TryGetProperty("4h", out _).Should().BeTrue("timeframes must contain '4h'");
        timeframes.TryGetProperty("1d", out _).Should().BeTrue("timeframes must contain '1d'");
    }

    [Fact]
    public async Task MarketFacts_Returns_Levels_Section()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var levels = json.RootElement.GetProperty("levels");

        levels.TryGetProperty("supports", out _).Should().BeTrue();
        levels.TryGetProperty("resistances", out _).Should().BeTrue();
    }

    [Fact]
    public async Task MarketFacts_Returns_MarketInternalSentiment_Section()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sentiment = json.RootElement.GetProperty("marketInternalSentiment");

        sentiment.TryGetProperty("longShortBiasScore", out _).Should().BeTrue();
        sentiment.TryGetProperty("fundingBiasScore", out _).Should().BeTrue();
        sentiment.TryGetProperty("orderBookPressureScore", out _).Should().BeTrue();
        sentiment.TryGetProperty("tradeFlowPressureScore", out _).Should().BeTrue();
        sentiment.TryGetProperty("marketRegime", out _).Should().BeTrue();
    }

    [Fact]
    public async Task MarketFacts_Returns_Source_With_Symbol()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("source").GetProperty("symbol").GetString()
            .Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task MarketFacts_Returns_AnalysisContext_With_Mode_And_Timeframes()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear&mode=Intraday");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ctx = json.RootElement.GetProperty("analysisContext");

        ctx.GetProperty("analysisMode").GetString().Should().Be("Intraday");
        ctx.GetProperty("primaryTimeframes").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("15m").And.Contain("1h").And.Contain("4h");
    }

    // ===========================================================================
    // Symbol trim
    // ===========================================================================

    [Fact]
    public async Task MarketFacts_Trims_Symbol_Before_Calling_Service()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service  = MockService(snapshot);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/%20BTCUSDT%20/market-facts?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        service.Verify(x => x.BuildSnapshotAsync(
            ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ===========================================================================
    // Mode defaulting
    // ===========================================================================

    [Fact]
    public async Task MarketFacts_Defaults_To_Intraday_When_Mode_Not_Specified()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("analysisContext").GetProperty("analysisMode").GetString()
            .Should().Be("Intraday");
    }

    [Fact]
    public async Task MarketFacts_With_Mode_Swing_Sets_Correct_PrimaryTimeframes()
    {
        var service = MockService(ApiSnapshotTestData.CreateSnapshot());

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear&mode=Swing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<MarketFactsPayload>();
        result!.AnalysisContext.AnalysisMode.Should().Be("Swing");
        result.AnalysisContext.PrimaryTimeframes.Should().Contain("1h")
            .And.Contain("4h")
            .And.Contain("1d");
    }

    // ===========================================================================
    // 400 Bad Request — validation
    // ===========================================================================

    [Fact]
    public async Task MarketFacts_Returns_BadRequest_When_Exchange_Is_Missing()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?category=Linear");

        await ProblemDetailsAssertions.AssertProblemAsync(
            response, HttpStatusCode.BadRequest, "Request validation failed.", "exchange");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarketFacts_Returns_BadRequest_When_Category_Is_Missing()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit");

        await ProblemDetailsAssertions.AssertProblemAsync(
            response, HttpStatusCode.BadRequest, "Request validation failed.", "category");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarketFacts_Returns_BadRequest_When_Symbol_Is_Whitespace()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        // %20 is a space — after trim the symbol is empty
        using var response = await client.GetAsync("/api/market-analysis/%20/market-facts?exchange=Bybit&category=Linear");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarketFacts_Returns_BadRequest_When_Mode_Is_Invalid()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear&mode=scalping");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.BadRequest);
        json.RootElement.GetProperty("errors").ToString().Should().Contain("mode");

        service.VerifyNoOtherCalls();
    }

    // ===========================================================================
    // 503 Service Unavailable
    // ===========================================================================

    [Fact]
    public async Task MarketFacts_Returns_ServiceUnavailable_When_Service_Throws_InvalidOperationException()
    {
        var service = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        service
            .Setup(x => x.BuildSnapshotAsync(
                ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ticker temporarily unavailable."));

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear");

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "Market facts analysis is temporarily unavailable.",
            "temporarily unavailable");
    }

    // ===========================================================================
    // Regression: old llm-payload endpoint must remain untouched
    // ===========================================================================

    [Fact]
    public async Task LlmPayload_Still_Returns_LlmPayload()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var service  = MockService(snapshot);

        using var client   = _factory.CreateClientWithMarketAnalysisService(service.Object);
        using var response = await client.GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=Intraday");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
        result.Should().NotBeNull();
        result.SchemaVersion.Should().Be("1.0",
            because: "llm-payload schema version must remain '1.0' and must not change to market-facts/v1");
        result.Symbol.Should().Be("BTCUSDT");

        // Confirm it's the LLM payload shape (has M15 as a property, not timeframes dict)
        using var json = JsonDocument.Parse(
            await _factory.CreateClientWithMarketAnalysisService(service.Object)
                .GetAsync("/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear")
                .ContinueWith(t => t.Result.Content.ReadAsStringAsync())
                .Unwrap());
        json.RootElement.TryGetProperty("m15", out _).Should().BeTrue(
            because: "llm-payload must still have 'm15' as a top-level property");
        json.RootElement.TryGetProperty("timeframes", out _).Should().BeFalse(
            because: "llm-payload must NOT have 'timeframes' dictionary — that is a market-facts shape");
    }

    // ===========================================================================
    // Helpers
    // ===========================================================================

    private static Mock<IMarketAnalysisService> MockService(
        Domain.Snapshots.MarketAnalysisSnapshot snapshot)
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
