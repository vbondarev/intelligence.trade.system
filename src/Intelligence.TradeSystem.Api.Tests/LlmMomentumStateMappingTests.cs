using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Проверяет формулу вычисления <c>summary.momentumState</c> в LLM payload.
/// Допустимые значения: Healthy | Weak | Overextended | Neutral.
/// Формула зависит от bias, isTrendConfirmed и положения RSI.
///
/// Один тестовый хост переиспользуется для всего класса через
/// <see cref="LlmMomentumStateTestFactory"/>. Каждый тест конфигурирует
/// <see cref="ConfigurableMarketAnalysisService"/> перед HTTP-запросом.
/// </summary>
public sealed class LlmMomentumStateMappingTests : IClassFixture<LlmMomentumStateTestFactory>, IDisposable
{
    private const string Url = "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear";

    private readonly LlmMomentumStateTestFactory _factory;
    private readonly HttpClient _client;

    public LlmMomentumStateMappingTests(LlmMomentumStateTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    // ─── Bullish: Healthy ────────────────────────────────────────────────────

    /// <summary>Confirmed Bullish trend + RSI in healthy zone [55, 70] → Healthy.</summary>
    [Theory]
    [InlineData(55)]   // lower boundary
    [InlineData(60)]   // typical healthy RSI
    [InlineData(70)]   // upper boundary
    public async Task MomentumState_Healthy_When_Bullish_Confirmed_And_Rsi_In_Zone(int rsi)
    {
        // Bullish confirmed: emaBullish=true (default), isAboveEma200=true (default)
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: rsi);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Healthy",
                because: $"{tf.Timeframe}: Bullish confirmed + RSI {rsi} → Healthy"));
    }

    // ─── Bullish: Overextended ───────────────────────────────────────────────

    [Fact]
    public async Task MomentumState_Overextended_When_Bullish_And_RsiOverbought_Flag()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: 68m, overrideRsiOverbought: true);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Overextended",
                because: $"{tf.Timeframe}: Bullish + rsiOverbought flag → Overextended"));
    }

    [Theory]
    [InlineData(71)]   // just above threshold
    [InlineData(80)]   // deeply overbought
    public async Task MomentumState_Overextended_When_Bullish_And_Rsi_Above_70(int rsi)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: rsi);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Overextended",
                because: $"{tf.Timeframe}: Bullish + RSI {rsi} > 70 → Overextended"));
    }

    // ─── Bullish: Weak ───────────────────────────────────────────────────────

    [Fact]
    public async Task MomentumState_Weak_When_Bullish_Unconfirmed()
    {
        // confirmed=false: emaBullish=true (bias stays Bullish) but isAboveEma200=false → isTrendConfirmed=false
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish,
            overrideIsAboveEma200: false, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: 60m);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Weak",
                because: $"{tf.Timeframe}: Bullish bias but not confirmed (belowEma200) → Weak"));
    }

    [Theory]
    [InlineData(40)]   // below 55
    [InlineData(54)]   // just below boundary
    public async Task MomentumState_Weak_When_Bullish_Confirmed_But_Rsi_Below_55(int rsi)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bullish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: rsi);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Weak",
                because: $"{tf.Timeframe}: Bullish confirmed but RSI {rsi} < 55 → Weak"));
    }

    // ─── Bearish: Healthy ────────────────────────────────────────────────────

    [Theory]
    [InlineData(30)]   // lower boundary
    [InlineData(38)]   // typical healthy bearish RSI
    [InlineData(45)]   // upper boundary
    public async Task MomentumState_Healthy_When_Bearish_Confirmed_And_Rsi_In_Zone(int rsi)
    {
        // Bearish confirmed: emaBearish=true (default), isAboveEma200=false (default)
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: rsi);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Healthy",
                because: $"{tf.Timeframe}: Bearish confirmed + RSI {rsi} → Healthy"));
    }

    // ─── Bearish: Overextended ───────────────────────────────────────────────

    [Fact]
    public async Task MomentumState_Overextended_When_Bearish_And_RsiOversold_Flag()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: 32m, overrideRsiOversold: true);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Overextended",
                because: $"{tf.Timeframe}: Bearish + rsiOversold flag → Overextended"));
    }

    [Theory]
    [InlineData(29)]   // just below threshold
    [InlineData(20)]   // deeply oversold
    public async Task MomentumState_Overextended_When_Bearish_And_Rsi_Below_30(int rsi)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: rsi);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Overextended",
                because: $"{tf.Timeframe}: Bearish + RSI {rsi} < 30 → Overextended"));
    }

    // ─── Bearish: Weak ───────────────────────────────────────────────────────

    [Fact]
    public async Task MomentumState_Weak_When_Bearish_Unconfirmed()
    {
        // confirmed=false: emaBearish=true (bias stays Bearish) but isAboveEma200=true → isTrendConfirmed=false
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish,
            overrideIsAboveEma200: true, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: 38m);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Weak",
                because: $"{tf.Timeframe}: Bearish bias but not confirmed (aboveEma200) → Weak"));
    }

    [Theory]
    [InlineData(46)]   // just above 45
    [InlineData(55)]   // clearly above bearish healthy zone
    public async Task MomentumState_Weak_When_Bearish_Confirmed_But_Rsi_Above_45(int rsi)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(
            MarketTrend.Bearish,
            overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null,
            overrideRsi14: rsi);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Weak",
                because: $"{tf.Timeframe}: Bearish confirmed but RSI {rsi} > 45 → Weak"));
    }

    // ─── Neutral bias → always Neutral ──────────────────────────────────────

    [Theory]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public async Task MomentumState_Neutral_For_Non_Directional_Trends(MarketTrend trend)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(trend);
        var result = await GetPayloadAsync(snapshot);

        AssertAllTimeframes(result!, tf =>
            tf.Summary.MomentumState.Should().Be("Neutral",
                because: $"{tf.Timeframe}: {trend} bias is Neutral → Neutral momentumState"));
    }

    // ─── Consistency: Healthy always implies confirmed bias ──────────────────

    [Theory]
    [InlineData(MarketTrend.Bullish)]
    [InlineData(MarketTrend.Bearish)]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public async Task MomentumState_Healthy_Implies_IsTrendConfirmed_And_Bias_Not_Neutral(MarketTrend trend)
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(trend);
        var result = await GetPayloadAsync(snapshot);

        foreach (var tf in new[] { result!.M15, result.H1, result.H4, result.D1 })
        {
            if (tf.Summary.MomentumState == "Healthy")
            {
                tf.Summary.IsTrendConfirmed.Should().BeTrue(
                    because: $"{tf.Timeframe}: Healthy momentum requires confirmed trend");
                tf.Summary.Bias.Should().NotBe("Neutral",
                    because: $"{tf.Timeframe}: Healthy momentum requires directional bias");
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<LlmMarketAnalysisPayload?> GetPayloadAsync(MarketAnalysisSnapshot snapshot)
    {
        _factory.MarketService.Configure(snapshot);
        using var response = await _client.GetAsync(Url);

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
