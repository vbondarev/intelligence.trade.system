using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        result.Portfolio.TotalEquityUsd.Should().Be(0m);
        result.Portfolio.AvailableBalanceUsd.Should().Be(0m);
        result.Portfolio.TotalWalletBalanceUsd.Should().Be(0m);
        result.Portfolio.TotalUnrealizedPnlUsd.Should().Be(0m);
        result.Portfolio.OpenPositions.Should().BeEmpty();
        result.Tags.Should().Equal("trend", "momentum");

        marketAnalysisService.Verify(
            x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Snapshot_Response_Uses_Public_Dto_Shape_Without_MarketData_Wrapper()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        root.GetProperty("portfolio").GetProperty("openPositions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Snapshot_Uses_Exact_Legacy_Root_Shape_With_Portfolio_And_String_Enums()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);
        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "Linear",
        });

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        JsonContractAssertions.AssertExactPropertyNames(root,
            "exchange", "symbol", "category", "capturedAtUtc", "price", "derivatives", "orderBook",
            "tradeFlow", "m15", "h1", "h4", "d1", "sentiment", "portfolio", "tags");
        root.TryGetProperty("marketData", out _).Should().BeFalse();
        AssertLegacyRootValueKinds(root);
        AssertLegacyPrice(root.GetProperty("price"));
        AssertLegacyDerivatives(root.GetProperty("derivatives"));
        AssertLegacyOrderBook(root.GetProperty("orderBook"));
        AssertLegacyTradeFlow(root.GetProperty("tradeFlow"));
        AssertLegacySentiment(root.GetProperty("sentiment"));
        AssertStringArray(root.GetProperty("tags"));

        foreach (var timeframe in new[] { "m15", "h1", "h4", "d1" })
            AssertLegacyTimeframe(root.GetProperty(timeframe));

        var portfolio = root.GetProperty("portfolio");
        JsonContractAssertions.AssertExactPropertyNames(portfolio,
            "totalEquityUsd", "availableBalanceUsd", "totalWalletBalanceUsd", "totalUnrealizedPnlUsd", "openPositions");
        portfolio.TryGetProperty("isAvailable", out _).Should().BeFalse();
        AssertNumberProperties(portfolio, "totalEquityUsd", "availableBalanceUsd", "totalWalletBalanceUsd", "totalUnrealizedPnlUsd");
        portfolio.GetProperty("totalEquityUsd").GetDecimal().Should().Be(0m);
        portfolio.GetProperty("availableBalanceUsd").GetDecimal().Should().Be(0m);
        portfolio.GetProperty("totalWalletBalanceUsd").GetDecimal().Should().Be(0m);
        portfolio.GetProperty("totalUnrealizedPnlUsd").GetDecimal().Should().Be(0m);
        portfolio.GetProperty("openPositions").ValueKind.Should().Be(JsonValueKind.Array);
        portfolio.GetProperty("openPositions").GetArrayLength().Should().Be(0);
        root.GetProperty("h1").GetProperty("trend").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static void AssertLegacyRootValueKinds(JsonElement root)
    {
        foreach (var propertyName in new[] { "exchange", "symbol", "category", "capturedAtUtc" })
            root.GetProperty(propertyName).ValueKind.Should().Be(JsonValueKind.String);

        foreach (var propertyName in new[] { "price", "derivatives", "orderBook", "tradeFlow", "m15", "h1", "h4", "d1", "sentiment", "portfolio" })
            root.GetProperty(propertyName).ValueKind.Should().Be(JsonValueKind.Object);
    }

    private static void AssertLegacyPrice(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "lastPrice", "markPrice", "indexPrice", "bidPrice", "askPrice",
            "bidSize", "askSize", "spreadAbs", "spreadPct", "price24hChangePct", "high24h", "low24h", "volume24h", "turnover24h");
        AssertNumberProperties(element, "lastPrice", "markPrice", "indexPrice", "bidPrice", "askPrice", "bidSize", "askSize",
            "spreadAbs", "spreadPct", "price24hChangePct", "high24h", "low24h", "volume24h", "turnover24h");
    }

    private static void AssertLegacyDerivatives(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "fundingRate", "nextFundingTimeUtc", "openInterest", "openInterestValue",
            "longRatio", "shortRatio", "premiumVsIndexPct", "openInterestChange1hPct", "openInterestChange4hPct", "fundingRateAvg24h");
        AssertNumberProperties(element, "fundingRate", "openInterest", "openInterestValue", "longRatio", "shortRatio",
            "premiumVsIndexPct", "openInterestChange1hPct", "openInterestChange4hPct", "fundingRateAvg24h");
        element.GetProperty("nextFundingTimeUtc").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static void AssertLegacyOrderBook(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "capturedAtUtc", "bestBidPrice", "bestAskPrice", "totalBidVolumeTop5",
            "totalAskVolumeTop5", "totalBidVolumeTop10", "totalAskVolumeTop10", "totalBidVolumeTop20", "totalAskVolumeTop20",
            "imbalanceTop5", "imbalanceTop10", "imbalanceTop20", "topBids", "topAsks", "bidWalls", "askWalls");
        element.GetProperty("capturedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(element, "bestBidPrice", "bestAskPrice", "totalBidVolumeTop5", "totalAskVolumeTop5", "totalBidVolumeTop10",
            "totalAskVolumeTop10", "totalBidVolumeTop20", "totalAskVolumeTop20", "imbalanceTop5", "imbalanceTop10", "imbalanceTop20");

        foreach (var propertyName in new[] { "topBids", "topAsks" })
        {
            var levels = element.GetProperty(propertyName);
            levels.ValueKind.Should().Be(JsonValueKind.Array);
            foreach (var level in levels.EnumerateArray())
            {
                JsonContractAssertions.AssertExactPropertyNames(level, "price", "size");
                AssertNumberProperties(level, "price", "size");
            }
        }

        foreach (var propertyName in new[] { "bidWalls", "askWalls" })
        {
            var walls = element.GetProperty(propertyName);
            walls.ValueKind.Should().Be(JsonValueKind.Array);
            foreach (var wall in walls.EnumerateArray())
            {
                JsonContractAssertions.AssertExactPropertyNames(wall, "price", "size", "distancePctFromMarket");
                AssertNumberProperties(wall, "price", "size", "distancePctFromMarket");
            }
        }
    }

    private static void AssertLegacyTradeFlow(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "windowStartUtc", "windowEndUtc", "buyVolume", "sellVolume", "deltaVolume",
            "deltaPct", "totalTrades", "buyTrades", "sellTrades", "avgTradeSize", "maxTradeSize", "hasAggressiveBuyPressure", "hasAggressiveSellPressure");
        element.GetProperty("windowStartUtc").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("windowEndUtc").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(element, "buyVolume", "sellVolume", "deltaVolume", "deltaPct", "totalTrades", "buyTrades", "sellTrades",
            "avgTradeSize", "maxTradeSize");
        element.GetProperty("hasAggressiveBuyPressure").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        element.GetProperty("hasAggressiveSellPressure").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    private static void AssertLegacyTimeframe(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "timeframe", "lastCandleOpenTimeUtc", "lastCandle", "ema20", "ema50",
            "ema200", "rsi14", "rsi14IsReliable", "atr14", "volumeSma20", "volumeRatio", "trendStrengthScore", "trend", "support1",
            "support2", "resistance1", "resistance2", "isAboveEma20", "isAboveEma50", "isAboveEma200", "emaBullishAlignment",
            "emaBearishAlignment", "rsiOverbought", "rsiOversold", "candleRangePct", "distanceToSupport1Pct", "distanceToResistance1Pct");
        element.GetProperty("timeframe").ValueKind.Should().Be(JsonValueKind.String);
        element.GetProperty("lastCandleOpenTimeUtc").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(element, "ema20", "ema50", "ema200", "rsi14", "atr14", "volumeSma20", "volumeRatio", "trendStrengthScore",
            "support1", "support2", "resistance1", "resistance2", "candleRangePct", "distanceToSupport1Pct", "distanceToResistance1Pct");
        element.GetProperty("trend").ValueKind.Should().Be(JsonValueKind.String);
        foreach (var propertyName in new[] { "rsi14IsReliable", "isAboveEma20", "isAboveEma50", "isAboveEma200", "emaBullishAlignment",
                     "emaBearishAlignment", "rsiOverbought", "rsiOversold" })
            element.GetProperty(propertyName).ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);

        var candle = element.GetProperty("lastCandle");
        JsonContractAssertions.AssertExactPropertyNames(candle, "openTimeUtc", "open", "high", "low", "close", "volume", "turnover");
        candle.GetProperty("openTimeUtc").ValueKind.Should().Be(JsonValueKind.String);
        AssertNumberProperties(candle, "open", "high", "low", "close", "volume", "turnover");
    }

    private static void AssertLegacySentiment(JsonElement element)
    {
        JsonContractAssertions.AssertExactPropertyNames(element, "longShortBiasScore", "fundingBiasScore", "orderBookPressureScore",
            "tradeFlowPressureScore", "marketRegime");
        AssertNumberProperties(element, "longShortBiasScore", "fundingBiasScore", "orderBookPressureScore", "tradeFlowPressureScore");
        element.GetProperty("marketRegime").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static void AssertStringArray(JsonElement array)
    {
        array.ValueKind.Should().Be(JsonValueKind.Array);
        foreach (var item in array.EnumerateArray())
            item.ValueKind.Should().Be(JsonValueKind.String);
    }

    private static void AssertNumberProperties(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            JsonContractAssertions.AssertValueKind(element.GetProperty(propertyName), JsonValueKind.Number, JsonValueKind.Null);
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Exchange_Is_Invalid()
    {
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "BTCUSDT",
            category = "futures",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.BadRequest);
        root.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        root.GetProperty("errors").ToString().Should().Contain("category");

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Category_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
    public async Task Snapshot_Trims_Symbol_Before_Calling_Service()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
        {
            exchange = "Bybit",
            symbol = "  BTCUSDT  ",
            category = "Linear",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        marketAnalysisService.Verify(
            x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Request_Body_Is_Missing()
    {
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsync("/api/market-analysis/snapshot", content: null);

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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);
        using var content = new StringContent("{ malformed json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/market-analysis/snapshot", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        marketAnalysisService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_Returns_BadRequest_When_Service_Throws_ArgumentException()
    {
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Symbol 'BTCUSDT' is invalid for snapshot analysis."));

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Exchange 'Bybit' is not supported in this environment."));

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
        var marketAnalysisService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        marketAnalysisService
            .Setup(x => x.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ticker is temporarily unavailable."));

        using var client = _factory.CreateClientWithMarketSnapshotService(marketAnalysisService.Object);

        using var response = await client.PostAsJsonAsync("/api/market-analysis/snapshot", new
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
