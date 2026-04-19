using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class SnapshotEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SnapshotEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Snapshot_Returns_Ok_And_MarketAnalysisResponse_When_Request_Is_Valid()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "bybit",
            symbol = "BTCUSDT",
            category = "linear",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<MarketAnalysisResponse>();
        result.Should().NotBeNull();
        result.Exchange.Should().Be("Bybit");
        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be("linear");
        result.Price.LastPrice.Should().Be(65000m);
        result.Derivatives.NextFundingTimeUtc.Should().Be(new DateTimeOffset(2026, 4, 12, 16, 0, 0, TimeSpan.Zero));
        result.OrderBook.TopBids.Should().ContainSingle(level => level.Price == 64995m && level.Size == 10m);
        result.TradeFlow.HasAggressiveBuyPressure.Should().BeTrue();
        result.M15.Timeframe.Should().Be("15m");
        result.H1.Trend.Should().Be("Bullish");
        result.Portfolio.OpenPositions.Should().ContainSingle(position => position.Symbol == "BTCUSDT" && position.Side == "Long");
        result.Tags.Should().Equal("trend", "momentum");

        marketAnalysisService.Verify(
            x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Snapshot_Response_Uses_Public_Dto_Shape_Without_MarketData_Wrapper()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.TryGetProperty("marketData", out _).Should().BeFalse();
        root.GetProperty("exchange").GetString().Should().Be("Bybit");
        root.GetProperty("price").GetProperty("lastPrice").GetDecimal().Should().Be(65000m);
        root.GetProperty("m15").GetProperty("timeframe").GetString().Should().Be("15m");
        root.GetProperty("h1").GetProperty("trend").GetString().Should().Be("Bullish");
        root.GetProperty("portfolio").GetProperty("openPositions").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Exchange_Is_Invalid()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "binance",
            symbol = "BTCUSDT",
            category = "linear",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.BadRequest);
        root.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        root.GetProperty("errors").ToString().Should().Contain("exchange");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Exchange_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            symbol = "BTCUSDT",
            category = "linear",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "exchange");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Category_Is_Invalid()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "futures",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "category");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Category_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "category");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Symbol_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = " ",
            category = "Linear",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "symbol");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Trims_Symbol_And_Category_Before_Calling_Service()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "  BTCUSDT  ",
            category = "  Linear  ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        marketAnalysisService.Verify(
            x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Request_Body_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsync("/api/analysis/snapshot", content: null);

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "Snapshot request body is required.");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Request_Body_Contains_Malformed_Json()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);
        using var content = new StringContent("{ malformed json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/analysis/snapshot", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Service_Throws_ArgumentException()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Symbol 'BTCUSDT' is invalid for snapshot analysis."));

        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "invalid for snapshot analysis");
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Service_Throws_NotSupportedException()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Exchange 'Bybit' is not supported in this environment."));

        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Request validation failed.",
            "not supported");
    }

    [Fact]
    public async Task Snapshot_Returns_ServiceUnavailable_When_Service_Throws_InvalidOperationException()
    {
        var marketAnalysisService = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ticker is temporarily unavailable."));

        using var client = _factory.CreateClientWithMarketAnalysisService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
        });

        await ProblemDetailsAssertions.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "Snapshot analysis is temporarily unavailable.",
            "Ticker is temporarily unavailable.");
    }

}


