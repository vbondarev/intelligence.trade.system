using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Проверяет формулу вычисления <c>summary.isTrendConfirmed</c> в LLM payload.
/// Формула:
/// - Bullish  : emaBullishAlignment == true &amp;&amp; isAboveEma200 == true
/// - Bearish  : emaBearishAlignment == true &amp;&amp; isAboveEma200 == false
/// - Sideways / Unknown : всегда false
/// </summary>
public sealed class LlmIsTrendConfirmedMappingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear";

    private readonly WebApplicationFactory<Program> _factory;

    public LlmIsTrendConfirmedMappingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── Bullish confirmed ───────────────────────────────────────────────────

    [Fact]
    public async Task IsTrendConfirmed_True_When_Bullish_With_EmaAlignment_And_AboveEma200()
    {
        // Bullish + emaBullishAlignment=true (default) + isAboveEma200=true (default)
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeTrue(
                because: $"{tf.Timeframe}: Bullish + emaBullish + aboveEma200 → confirmed"));
    }

    // ─── Bullish unconfirmed ─────────────────────────────────────────────────

    [Fact]
    public async Task IsTrendConfirmed_False_When_Bullish_But_EmaBullishAlignmentFalse()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish, overrideIsAboveEma200: null, overrideEmaBullish: false, overrideEmaBearish: null);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeFalse(
                because: $"{tf.Timeframe}: Bullish without EMA alignment → not confirmed"));
    }

    [Fact]
    public async Task IsTrendConfirmed_False_When_Bullish_But_BelowEma200()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish, overrideIsAboveEma200: false, overrideEmaBullish: null, overrideEmaBearish: null);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeFalse(
                because: $"{tf.Timeframe}: Bullish but price below EMA200 → not confirmed"));
    }

    // ─── Bearish confirmed ───────────────────────────────────────────────────

    [Fact]
    public async Task IsTrendConfirmed_True_When_Bearish_With_EmaAlignment_And_BelowEma200()
    {
        // Bearish default: emaBearishAlignment=true, isAboveEma200=false
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bearish);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeTrue(
                because: $"{tf.Timeframe}: Bearish + emaBearish + belowEma200 → confirmed"));
    }

    // ─── Bearish unconfirmed ─────────────────────────────────────────────────

    [Fact]
    public async Task IsTrendConfirmed_False_When_Bearish_But_EmaBearishAlignmentFalse()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish, overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: false);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeFalse(
                because: $"{tf.Timeframe}: Bearish without EMA alignment → not confirmed"));
    }

    [Fact]
    public async Task IsTrendConfirmed_False_When_Bearish_But_AboveEma200()
    {
        // emaBearishAlignment=true (default for Bearish), but price is above EMA200 — conflict
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish, overrideIsAboveEma200: true, overrideEmaBullish: null, overrideEmaBearish: null);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeFalse(
                because: $"{tf.Timeframe}: Bearish but price above EMA200 → not confirmed"));
    }

    // ─── Neutral trends always false ─────────────────────────────────────────

    [Theory]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public async Task IsTrendConfirmed_False_For_Non_Directional_Trends(MarketTrend trend)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(trend);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.IsTrendConfirmed.Should().BeFalse(
                because: $"{tf.Timeframe}: {trend} has no directional trend to confirm"));
    }

    // ─── Consistency: isTrendConfirmed does not contradict bias ──────────────

    [Theory]
    [InlineData(MarketTrend.Bullish)]
    [InlineData(MarketTrend.Bearish)]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public async Task IsTrendConfirmed_True_Implies_Bias_Is_Not_Neutral(MarketTrend trend)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(trend);
        var result = await GetPayloadAsync(snapshot);

        foreach (var tf in new[] { result!.M15, result.H1, result.H4, result.D1 })
        {
            if (tf.Summary.IsTrendConfirmed)
            {
                tf.Summary.Bias.Should().NotBe("Neutral",
                    because: $"{tf.Timeframe}: confirmed trend must not produce Neutral bias");
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<LlmMarketAnalysisPayload?> GetPayloadAsync(MarketSnapshot snapshot)
    {
        var mock = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        mock.Setup(x => x.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        using var client = _factory.CreateClientWithMarketSnapshotService(mock.Object);
        using var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
    }

    private static void AssertAllTimeframes(
        LlmMarketAnalysisPayload result,
        Action<LlmTimeframePayload> assertion)
    {
        foreach (var tf in new[] { result.M15, result.H1, result.H4, result.D1 })
        {
            assertion(tf);
        }
    }
}
