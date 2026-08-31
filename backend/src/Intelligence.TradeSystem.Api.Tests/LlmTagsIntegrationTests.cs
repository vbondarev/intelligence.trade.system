using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.MarketIntelligence.Analysis;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Integration-тесты для поля <c>tags</c> в LLM payload.
/// Проверяют, что теги детерминированы, не противоречат друг другу,
/// не превышают лимит и идут в стабильном порядке.
/// </summary>
public sealed class LlmTagsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url =
        "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear";

    private static readonly IReadOnlyList<string> _allowedTags =
    [
        MarketTagConstants.Trending,
        MarketTagConstants.Neutral,
        MarketTagConstants.PositiveFunding,
        MarketTagConstants.NegativeFunding,
        MarketTagConstants.BidPressure,
        MarketTagConstants.AskPressure,
        MarketTagConstants.AggressiveBuying,
        MarketTagConstants.AggressiveSelling,
        MarketTagConstants.VolatileRegime,
        MarketTagConstants.BullishRegime,
        MarketTagConstants.BearishRegime,
        MarketTagConstants.UnknownMarketRegime,
        MarketTagConstants.StaleSnapshot,
        MarketTagConstants.StaleOrderBook,
        MarketTagConstants.StaleTradeFlow,
        MarketTagConstants.ShortTradeFlowWindow,
        MarketTagConstants.LowTradeFlowVolume,
        MarketTagConstants.OrderBookTradeFlowConflict,
        MarketTagConstants.WeakTradeFlowConfirmation,
        MarketTagConstants.StrongOrderBookImbalance,
        MarketTagConstants.UpperLiquidityHeavy,
        MarketTagConstants.LowerLiquidityHeavy,
        MarketTagConstants.OiDeclining,
        MarketTagConstants.OiRising,
        MarketTagConstants.LongCrowded,
        MarketTagConstants.ShortCrowded,
        MarketTagConstants.PossibleShortCovering,
        MarketTagConstants.PossibleLongUnwinding,
        MarketTagConstants.NeutralFunding,
        MarketTagConstants.Near24hHigh,
        MarketTagConstants.Near24hLow,
        MarketTagConstants.LowVolume,
        MarketTagConstants.RsiOverbought,
        MarketTagConstants.RsiOversold,
        MarketTagConstants.WeakTrend,
        MarketTagConstants.RangeBound,
        MarketTagConstants.NeutralTimeframes,
        MarketTagConstants.NearResistance,
        MarketTagConstants.NearSupport,
        MarketTagConstants.OverextendedMomentum,
        MarketTagConstants.DirectionalTrendWithNeutralRegime,
        MarketTagConstants.NoCleanEntry,
        MarketTagConstants.ActionableEntry,
        MarketTagConstants.WeakEntryConfirmation,
        MarketTagConstants.TrendConfirmedEntryFiltered,
    ];

    private readonly WebApplicationFactory<Program> _factory;

    public LlmTagsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── Базовые контракты ────────────────────────────────────────────────────

    [Fact]
    public async Task Tags_Are_Present_In_Response_Json()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());
        tags.Should().NotBeNull();
    }

    [Fact]
    public async Task Tags_Count_Does_Not_Exceed_MaxTags()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        tags.Count.Should().BeInRange(0, MarketTagConstants.MaxTags,
            because: "лимит V2 = 20 тегов");
    }

    [Fact]
    public async Task Tags_Contain_No_Duplicates()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        tags.Should().OnlyHaveUniqueItems(because: "теги не должны дублироваться");
    }

    [Fact]
    public async Task Tags_Contain_Only_Allowed_Whitelist_Values()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        foreach (var tag in tags)
        {
            _allowedTags.Should().Contain(tag,
                because: $"тег '{tag}' отсутствует в V2 whitelist");
        }
    }

    // ─── Конкретные значения для известного снапшота ─────────────────────────

    [Fact]
    public async Task Tags_Contain_Trending_When_MarketRegime_Is_Trending()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        tags.Should().Contain("trending");
    }

    [Fact]
    public async Task Tags_Contain_PositiveFunding_When_FundingRate_Is_Positive()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        tags.Should().Contain("positive-funding");
    }

    [Fact]
    public async Task Tags_Are_Deterministic_For_Same_Snapshot()
    {
        var snapshot = BuildKnownSnapshot();
        using var client1 = CreateClientWithSnapshot(snapshot);
        using var client2 = CreateClientWithSnapshot(snapshot);

        var tags1 = ParseTags(await (await client1.GetAsync(Url)).Content.ReadAsStringAsync());
        var tags2 = ParseTags(await (await client2.GetAsync(Url)).Content.ReadAsStringAsync());

        tags1.Should().Equal(tags2,
            because: "одинаковый снапшот всегда должен возвращать одинаковые теги");
    }

    [Fact]
    public async Task Tags_Can_Contain_V2_EntryQuality_Tags_From_Enricher()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            Tags = ["positive-funding"],
        };
        using var client = CreateClientWithSnapshot(snapshot);
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        tags.Should().Contain("positive-funding");
        tags.Should().Contain(MarketTagConstants.NoCleanEntry);
    }

    // ─── Conflicting tags never co-exist ─────────────────────────────────────

    [Fact]
    public async Task Tags_Never_Contain_Both_Trending_And_Neutral()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        var hasConflict = tags.Contains("trending") && tags.Contains("neutral");
        hasConflict.Should().BeFalse(because: "trending и neutral — взаимоисключающие теги");
    }

    [Fact]
    public async Task Tags_Never_Contain_Both_BidPressure_And_AskPressure()
    {
        using var client = CreateClientWithSnapshot(BuildKnownSnapshot());
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        var hasConflict = tags.Contains("bid-pressure") && tags.Contains("ask-pressure");
        hasConflict.Should().BeFalse(because: "bid-pressure и ask-pressure — взаимоисключающие теги");
    }

    // ─── Сценарий 8: trend confirmed but entry filtered ───────────────────────

    [Fact]
    public async Task Tags_Contain_TrendConfirmedEntryFiltered_And_WeakEntryConfirmation_When_Trend_Confirmed_But_Entry_Poor()
    {
        // Bullish snapshot with EmaBullishAlignment=true and IsAboveEma200=true → IsTrendConfirmed=true.
        // Stale CapturedAtUtc causes EntryQuality=Poor, so LlmTimeframeSummaryBuilder adds
        // "TrendConfirmedButEntryFiltered" to riskFlags. LlmTagEnricher maps it to
        // "trend-confirmed-entry-filtered". AggressiveBuying in base tags triggers
        // "weak-entry-confirmation" (directional signal + Poor entry).
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            Tags = [MarketTagConstants.AggressiveBuying],
        };

        using var client = CreateClientWithSnapshot(snapshot);
        using var response = await client.GetAsync(Url);

        var tags = ParseTags(await response.Content.ReadAsStringAsync());

        tags.Should().Contain(MarketTagConstants.TrendConfirmedEntryFiltered);
        tags.Should().Contain(MarketTagConstants.WeakEntryConfirmation);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Снапшот с известными входными данными, которые должны дать конкретный набор тегов:
    /// trending, positive-funding (без давления и агрессии из тестовых данных).
    /// </summary>
    private static MarketAnalysisSnapshot BuildKnownSnapshot()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();

        return snapshot with
        {
            Derivatives = snapshot.Derivatives with { FundingRate = 0.0001m },
            OrderBook = snapshot.OrderBook with { ImbalanceTop5 = 0m },   // нет давления
            TradeFlow = snapshot.TradeFlow with
            {
                HasAggressiveBuyPressure = false,
                HasAggressiveSellPressure = false,
            },
            Sentiment = snapshot.Sentiment with { MarketRegime = "Trending" },
            Tags = ["trending", "positive-funding"],  // pre-built tags from test fixture
        };
    }

    private HttpClient CreateClientWithSnapshot(MarketAnalysisSnapshot snapshot)
    {
        var mock = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        mock.Setup(x => x.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        return _factory.CreateClientWithMarketAnalysisService(mock.Object);
    }

    private static List<string> ParseTags(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tagsElem = doc.RootElement.GetProperty("tags");

        return tagsElem.EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
    }
}
