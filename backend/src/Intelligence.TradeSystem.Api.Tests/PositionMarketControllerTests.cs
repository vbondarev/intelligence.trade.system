using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions.Market;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionMarketControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public PositionMarketControllerTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Market_returns_explicit_position_scoped_context_without_private_fields()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var identityStore = CreateIdentityStore(userId, identity);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        snapshotService
            .Setup(service => service.BuildSnapshotAsync(
                identity.ExchangeId,
                identity.Symbol,
                identity.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        var service = CreateService(identityStore, snapshotService, new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/market");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PositionMarketResponse>(
            V1JsonSerializerOptions.Default);
        body.Should().NotBeNull();
        body!.PositionId.Should().Be(identity.PositionId.Value);
        body.Exchange.Should().Be(ExchangeProvider.Bybit);
        body.Symbol.Should().Be(identity.Symbol);
        body.MarketCategory.Should().Be(MarketCategoryV1.Linear);
        body.Price.LastPrice.Should().Be(snapshot.Price.LastPrice);
        body.M15.Trend.Should().Be(MarketTrendV1.Bullish);
        body.Sentiment.MarketRegime.Should().Be(MarketRegimeV1.Trending);
        body.Tags.Should().Equal("trend", "momentum");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny(
            "indicatorDiagnostics",
            "portfolio",
            "assessment",
            "recommendation",
            "userId",
            "credentials");
    }

    [Fact]
    public async Task Market_preserves_nullable_fields_as_explicit_json_nulls()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var baseline = ApiSnapshotTestData.CreateSnapshot();
        var snapshot = baseline with
        {
            Derivatives = baseline.Derivatives with
            {
                NextFundingTimeUtc = null,
                PremiumVsIndexPct = null,
            },
            M15 = baseline.M15 with
            {
                Ema20 = null,
                Rsi14 = null,
                Support1 = null,
                DistanceToSupport1Pct = null,
            },
        };
        var identityStore = CreateIdentityStore(userId, identity);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        snapshotService
            .Setup(service => service.BuildSnapshotAsync(
                identity.ExchangeId,
                identity.Symbol,
                identity.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        var service = CreateService(
            identityStore,
            snapshotService,
            new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/market");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var derivatives = root.GetProperty("derivatives");
        var m15 = root.GetProperty("m15");

        AssertJsonNull(derivatives, "nextFundingTime");
        AssertJsonNull(derivatives, "premiumVsIndexPct");
        AssertJsonNull(m15, "ema20");
        AssertJsonNull(m15, "rsi14");
        AssertJsonNull(m15, "support1");
        AssertJsonNull(m15, "distanceToSupport1Pct");
    }

    [Fact]
    public async Task Market_returns_same_not_found_problem_for_missing_and_foreign_positions()
    {
        var userId = UserId.New();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(
                userId,
                It.IsAny<PositionId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionMarketIdentity?)null);
        var service = CreateService(
            identityStore,
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);

        using var missingResponse = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/market");
        using var foreignResponse = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/market");

        var missing = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var foreign = await foreignResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreign!.Type.Should().Be(missing!.Type);
        foreign.Title.Should().Be(missing.Title);
        foreign.Detail.Should().Be(missing.Detail);
        foreign.Extensions["code"]!.ToString().Should().Be("resource_not_found");
    }

    [Fact]
    public async Task Market_rejects_empty_and_malformed_position_ids()
    {
        var userId = UserId.New();
        var service = CreateService(
            new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict),
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);

        using var emptyResponse = await client.GetAsync(
            $"/api/v1/positions/{Guid.Empty}/market");
        using var malformedResponse = await client.GetAsync(
            "/api/v1/positions/not-a-guid/market");

        await AssertValidationProblem(emptyResponse);
        await AssertValidationProblem(malformedResponse);
    }

    [Fact]
    public async Task Candles_return_not_found_without_provider_io_for_missing_position()
    {
        var userId = UserId.New();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        identityStore
            .Setup(store => store.GetAsync(
                userId,
                It.IsAny<PositionId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionMarketIdentity?)null);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        var service = CreateService(
            identityStore,
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            marketDataProvider);
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/candles?interval=15m");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        problem!.Extensions["code"]!.ToString().Should().Be("resource_not_found");
        marketDataProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Candles_missing_interval_returns_validation_problem_without_market_io()
    {
        var userId = UserId.New();
        var identityStore = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        var service = CreateService(identityStore, snapshotService, marketDataProvider);
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/candles");

        await AssertValidationProblem(response);
        identityStore.VerifyNoOtherCalls();
        snapshotService.VerifyNoOtherCalls();
        marketDataProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Market_data_unavailable_uses_the_stable_503_problem()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var identityStore = CreateIdentityStore(userId, identity);
        var snapshotService = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        snapshotService
            .Setup(service => service.BuildSnapshotAsync(
                identity.ExchangeId,
                identity.Symbol,
                identity.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MarketDataUnavailableException("market unavailable"));
        var service = CreateService(identityStore, snapshotService, new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/market");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        problem!.Extensions["code"]!.ToString().Should().Be("market_data_unavailable");
    }

    [Fact]
    public async Task Candles_use_persisted_identity_and_return_oldest_first()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var identityStore = CreateIdentityStore(userId, identity);
        var first = CreateKline(identity, KlineInterval.FifteenMinutes, 1);
        var second = CreateKline(identity, KlineInterval.FifteenMinutes, 2);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                KlineInterval.FifteenMinutes,
                null,
                null,
                200,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([second, first]);
        var service = CreateService(
            identityStore,
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            marketDataProvider);
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/candles?interval=15m");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PositionCandlesResponse>(
            V1JsonSerializerOptions.Default);
        body.Should().NotBeNull();
        body!.Interval.Should().Be("15m");
        body.Items.Select(item => item.OpenTimeUtc).Should().BeInAscendingOrder();
        body.Items.Select(item => item.Close).Should().Equal(first.Close, second.Close);
        marketDataProvider.VerifyAll();
    }

    [Fact]
    public async Task Candles_ignore_query_market_identity_overrides_and_forward_explicit_limit()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var identityStore = CreateIdentityStore(userId, identity);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                KlineInterval.OneHour,
                null,
                null,
                37,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateKline(identity, KlineInterval.OneHour, 1)]);
        var service = CreateService(
            identityStore,
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            marketDataProvider);
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/candles" +
            "?interval=1h&limit=37&symbol=ETHUSDT&exchange=other&marketCategory=spot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PositionCandlesResponse>(
            V1JsonSerializerOptions.Default);
        body!.Symbol.Should().Be(identity.Symbol);
        body.MarketCategory.Should().Be(MarketCategoryV1.Linear);
        marketDataProvider.VerifyAll();
    }

    [Theory]
    [InlineData("1m")]
    [InlineData("3m")]
    [InlineData("5m")]
    [InlineData("15m")]
    [InlineData("30m")]
    [InlineData("1h")]
    [InlineData("2h")]
    [InlineData("4h")]
    [InlineData("6h")]
    [InlineData("12h")]
    [InlineData("1d")]
    [InlineData("1w")]
    [InlineData("1mo")]
    public async Task Candles_accept_all_supported_intervals(string wireInterval)
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        CandleIntervalV1Codec.TryParse(wireInterval, out var interval).Should().BeTrue();
        var identityStore = CreateIdentityStore(userId, identity);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                interval,
                null,
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateKline(identity, interval, 1)]);
        var service = CreateService(
            identityStore,
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            marketDataProvider);
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/candles?interval={wireInterval}&limit=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PositionCandlesResponse>(
            V1JsonSerializerOptions.Default);
        body!.Interval.Should().Be(wireInterval);
        marketDataProvider.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("15M")]
    [InlineData("FifteenMinutes")]
    [InlineData("interval=15m&interval=1h")]
    [InlineData("interval=15m&limit=0")]
    public async Task Candles_reject_invalid_query_values_without_market_io(string query)
    {
        var userId = UserId.New();
        var service = CreateService(
            new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict),
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);
        var queryString = query.Contains('=')
            ? query
            : $"interval={Uri.EscapeDataString(query)}";

        using var response = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/candles?{queryString}");

        await AssertValidationProblem(response);
    }

    [Theory]
    [InlineData("limit=")]
    [InlineData("limit=abc")]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    [InlineData("limit=1&limit=2")]
    public async Task Candles_reject_invalid_limits_without_market_io(string limitQuery)
    {
        var userId = UserId.New();
        var service = CreateService(
            new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict),
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            new Mock<IMarketDataProvider>(MockBehavior.Strict));
        using var client = CreateClient(userId, service);
        var query = string.IsNullOrEmpty(limitQuery)
            ? "interval=15m"
            : $"interval=15m&{limitQuery}";

        using var response = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/candles?{query}");

        await AssertValidationProblem(response);
    }

    [Fact]
    public async Task Empty_upstream_candles_return_503()
    {
        var userId = UserId.New();
        var identity = CreateIdentity();
        var identityStore = CreateIdentityStore(userId, identity);
        var marketDataProvider = new Mock<IMarketDataProvider>(MockBehavior.Strict);
        marketDataProvider
            .Setup(provider => provider.GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                KlineInterval.FifteenMinutes,
                null,
                null,
                200,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = CreateService(
            identityStore,
            new Mock<IMarketSnapshotService>(MockBehavior.Strict),
            marketDataProvider);
        using var client = CreateClient(userId, service);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{identity.PositionId.Value}/candles?interval=15m");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        problem!.Extensions["code"]!.ToString().Should().Be("market_data_unavailable");
    }

    private HttpClient CreateClient(UserId userId, PositionMarketService service)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PositionMarketService>();
                services.AddSingleton(service);
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.Value.ToString());
        return client;
    }

    private static PositionMarketService CreateService(
        Mock<IPositionMarketIdentityStore> identityStore,
        Mock<IMarketSnapshotService> snapshotService,
        Mock<IMarketDataProvider> marketDataProvider) =>
        new(identityStore.Object, snapshotService.Object, marketDataProvider.Object);

    private static Mock<IPositionMarketIdentityStore> CreateIdentityStore(
        UserId userId,
        PositionMarketIdentity identity)
    {
        var store = new Mock<IPositionMarketIdentityStore>(MockBehavior.Strict);
        store
            .Setup(item => item.GetAsync(
                userId,
                identity.PositionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);
        return store;
    }

    private static PositionMarketIdentity CreateIdentity() => new(
        PositionId.New(),
        ExchangeAccountId.New(),
        ExchangeId.Bybit,
        "BTCUSDT",
        MarketCategory.Linear);

    private static Kline CreateKline(
        PositionMarketIdentity identity,
        KlineInterval interval,
        int minute) =>
        new(
            identity.Symbol,
            identity.MarketCategory,
            interval,
            new DateTime(2026, 9, 20, 10, minute, 0, DateTimeKind.Unspecified),
            100m + minute,
            110m + minute,
            90m + minute,
            105m + minute,
            10m,
            1_000m);

    private static async Task AssertValidationProblem(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
    }

    private static void AssertJsonNull(JsonElement parent, string propertyName)
    {
        parent.TryGetProperty(propertyName, out var property).Should().BeTrue();
        property.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
