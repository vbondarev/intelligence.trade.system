using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Integration-тесты для мягких предупреждений <c>snapshotHealth.warnings</c>.
/// Проверяют, что snapshot остаётся <c>isFresh=true, isPartial=false</c>,
/// но <c>warnings</c> при этом не пустой, когда в данных есть ограничения интерпретации.
/// </summary>
public sealed class SnapshotHealthInterpretationWarningsTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url =
        "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear";

    private readonly WebApplicationFactory<Program> _factory;

    public SnapshotHealthInterpretationWarningsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── Snapshot остаётся здоровым ───────────────────────────────────────────

    [Fact]
    public async Task IsFresh_Remains_True_When_Only_Interpretation_Warnings_Present()
    {
        using var client = CreateClientWithConflictingSnapshot();
        using var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var health = json.RootElement.GetProperty("snapshotHealth");

        health.GetProperty("isFresh").GetBoolean().Should().BeTrue(
            because: "мягкие warnings не должны переводить snapshot в stale");
    }

    [Fact]
    public async Task IsPartial_Remains_False_When_Only_Interpretation_Warnings_Present()
    {
        using var client = CreateClientWithConflictingSnapshot();
        using var response = await client.GetAsync(Url);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var health = json.RootElement.GetProperty("snapshotHealth");

        health.GetProperty("isPartial").GetBoolean().Should().BeFalse();
    }

    // ─── Warnings не пустые ───────────────────────────────────────────────────

    [Fact]
    public async Task Warnings_Contains_ConflictingMicrostructure_When_Scores_Conflict()
    {
        using var client = CreateClientWithConflictingSnapshot();
        using var response = await client.GetAsync(Url);

        var warnings = ParseWarnings(await response.Content.ReadAsStringAsync());

        warnings.Should().Contain("orderBook and tradeFlow signals are conflicting",
            because: "OrderBookPressureScore > 0 и TradeFlowPressureScore < 0");
    }

    [Fact]
    public async Task Warnings_Contains_LowVolume_When_VolumeRatio_Below_Threshold()
    {
        using var client = CreateClientWithLowVolumeSnapshot();
        using var response = await client.GetAsync(Url);

        var warnings = ParseWarnings(await response.Content.ReadAsStringAsync());

        warnings.Should().Contain("low volume on primary timeframes",
            because: "VolumeRatio = 0.3 < 0.5 на первичных таймфреймах");
    }


    [Fact]
    public async Task Warnings_NotEmpty_When_Multiple_Interpretation_Rules_Fire()
    {
        using var client = CreateClientWithConflictingSnapshot();
        using var response = await client.GetAsync(Url);

        var warnings = ParseWarnings(await response.Content.ReadAsStringAsync());

        warnings.Should().NotBeEmpty(because: "при конфликтующих данных должны появиться предупреждения");
        warnings.Count.Should().BeInRange(1, 5, because: "список warnings урезается до максимум 5");
    }

    [Fact]
    public async Task Warnings_NotContain_Duplicates()
    {
        using var client = CreateClientWithConflictingSnapshot();
        using var response = await client.GetAsync(Url);

        var warnings = ParseWarnings(await response.Content.ReadAsStringAsync());

        warnings.Should().OnlyHaveUniqueItems(because: "предупреждения не должны дублироваться");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private HttpClient CreateClientWithConflictingSnapshot()
    {
        // OrderBook и TradeFlow дают противоположные сигналы
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish) with
        {
            Sentiment = ApiSnapshotTestData.CreateSnapshot().Sentiment with
            {
                OrderBookPressureScore = 0.3m,
                TradeFlowPressureScore = -0.25m,
                MarketRegime = "Trending",
            },
        };
        return CreateClientWithSnapshot(snapshot);
    }

    private HttpClient CreateClientWithLowVolumeSnapshot()
    {
        var baseSnapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish);
        var lowTf = baseSnapshot.M15 with { VolumeRatio = 0.3m };
        var snapshot = baseSnapshot with
        {
            M15 = lowTf,
            H1 = lowTf with { Timeframe = "1h" },
            H4 = lowTf with { Timeframe = "4h" },
        };
        return CreateClientWithSnapshot(snapshot);
    }

    private HttpClient CreateClientWithSnapshot(MarketSnapshot snapshot)
    {
        var mock = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        mock.Setup(x => x.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        return _factory.CreateClientWithMarketSnapshotService(mock.Object);
    }

    private static List<string> ParseWarnings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var health = doc.RootElement.GetProperty("snapshotHealth");
        var warningsElem = health.GetProperty("warnings");

        return warningsElem.EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
    }
}
