using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Ai;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class AiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ai_Returns_Ok_And_Analysis_Response_When_Request_Is_Valid()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);
        llmAnalyticsService
            .Setup(x => x.AnalyzeAsync(snapshot, "intraday outlook", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Bullish intraday bias with elevated risk.");

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = "intraday outlook",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AiAnalysisResponse>();
        result.Should().NotBeNull();
        result.Exchange.Should().Be("Bybit");
        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be("linear");
        result.Analysis.Should().Be("Bullish intraday bias with elevated risk.");

        marketAnalysisService.Verify(
            x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
        llmAnalyticsService.Verify(
            x => x.AnalyzeAsync(snapshot, "intraday outlook", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Ai_Returns_BadRequest_When_UserQuery_Is_Missing_And_Does_Not_Call_Dependencies()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = " ",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "userQuery");

        marketAnalysisService.VerifyNoOtherCalls();
        llmAnalyticsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ai_Returns_BadRequest_When_Request_Body_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsync("/api/market-analysis/ai", content: null);

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "AI analysis request body is required.");

        marketAnalysisService.VerifyNoOtherCalls();
        llmAnalyticsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ai_Trims_Symbol_And_UserQuery_Before_Calling_Dependencies()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);
        llmAnalyticsService
            .Setup(x => x.AnalyzeAsync(snapshot, "intraday outlook", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Bullish intraday bias with elevated risk.");

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "  BTCUSDT  ",
            category = "Linear",
            userQuery = "  intraday outlook  ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        marketAnalysisService.Verify(
            x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
        llmAnalyticsService.Verify(
            x => x.AnalyzeAsync(snapshot, "intraday outlook", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Ai_Returns_BadRequest_When_Exchange_Is_Invalid_And_Does_Not_Call_Dependencies()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "binance",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = "intraday outlook",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.BadRequest);
        root.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        root.GetProperty("errors").ToString().Should().Contain("exchange");

        marketAnalysisService.VerifyNoOtherCalls();
        llmAnalyticsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ai_Returns_BadRequest_When_Category_Is_Invalid_And_Does_Not_Call_Dependencies()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "futures",
            userQuery = "intraday outlook",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.BadRequest);
        root.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        root.GetProperty("errors").ToString().Should().Contain("category");

        marketAnalysisService.VerifyNoOtherCalls();
        llmAnalyticsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ai_Returns_ServiceUnavailable_When_Snapshot_Service_Throws_InvalidOperationException_And_Does_Not_Call_Llm()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Snapshot data is temporarily unavailable."));

        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = "intraday outlook",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "AI analysis is temporarily unavailable.",
            "Snapshot data is temporarily unavailable.");

        llmAnalyticsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ai_Returns_BadRequest_When_Snapshot_Service_Throws_ArgumentException_And_Does_Not_Call_Llm()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Symbol 'BTCUSDT' is invalid for AI analysis."));

        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = "intraday outlook",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "invalid for AI analysis");

        llmAnalyticsService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ai_Returns_BadGateway_When_Llm_Service_Throws_HttpRequestException()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);
        llmAnalyticsService
            .Setup(x => x.AnalyzeAsync(snapshot, "intraday outlook", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("OpenRouter returned 401 Unauthorized."));

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = "intraday outlook",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadGateway,
            "AI provider request failed.",
            "401 Unauthorized");
    }

    [Fact]
    public async Task Ai_Returns_ServiceUnavailable_When_Llm_Service_Throws_InvalidOperationException()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var llmAnalyticsService = new Mock<ILlmAnalyticsService>(MockBehavior.Strict);
        llmAnalyticsService
            .Setup(x => x.AnalyzeAsync(snapshot, "intraday outlook", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM provider returned an empty response."));

        using var client = _factory.CreateClientWithAnalysisServices(marketAnalysisService.Object, llmAnalyticsService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/ai", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
            userQuery = "intraday outlook",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "AI analysis is temporarily unavailable.",
            "empty response");
    }
}
