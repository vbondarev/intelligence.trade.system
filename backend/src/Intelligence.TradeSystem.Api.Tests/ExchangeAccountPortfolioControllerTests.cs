using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class ExchangeAccountPortfolioControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ExchangeAccountPortfolioControllerTests(ApiWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Get_returns_summary_without_embedded_positions()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var positions = Enumerable.Range(0, 3)
            .Select(_ => new PositionMarketValuationInput(
                PositionId.New(),
                ExchangeId.Bybit,
                MarketCategory.Inverse,
                "BTCUSD",
                SettlementAsset.From("USDT"),
                PositionTrackingState.Active,
                1m))
            .ToArray();
        var store = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        store.Setup(x => x.GetLatestAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(true, new PortfolioReadSummary(
                accountId,
                new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
                1000m,
                800m,
                1000m,
                new DateTimeOffset(2026, 9, 20, 9, 59, 0, TimeSpan.Zero),
                200m,
                120m,
                80m,
                40m,
                10m,
                200m,
                800m,
                80m,
                20m,
                60m,
                null,
                true,
                true,
                true,
                3,
                [
                    new PortfolioExposureReadSummary(SettlementAsset.From("USDC"), 50m, 50m, 0m),
                    new PortfolioExposureReadSummary(SettlementAsset.From("USDT"), null, null, 30m),
                ])
            {
                SnapshotPositionIds = positions.Select(position => position.Id).ToArray(),
            }));
        using var client = CreateClient(userId, store.Object, positions);

        using var response = await client.GetAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(
            V1JsonSerializerOptions.Default);
        body!.ExchangeAccountId.Should().Be(accountId.Value);
        body.Capital.TotalEquity.Should().Be(1000m);
        body.TotalUnrealizedPnl.Should().Be(10m);
        body.LargestPositionId.Should().BeNull();
        body.CurrentPositionCount.Should().Be(3);
        body.FactualFreshness.Status.Should().Be(FactualFreshnessStatusV1.Unknown);
        body.ValuationFreshness.Status.Should().Be(MarketValuationFreshnessStatusV1.NotApplicable);
        body.ValuationFreshness.CalculatedAt.Should().NotBeNull();
        body.Exposures.Should().Equal(
            new PortfolioExposureResponse("USDC", 50m, 50m, 0m),
            new PortfolioExposureResponse("USDT", null, null, 30m));
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("\"currentPositionCount\":3");
        raw.Should().Contain("\"settlementAsset\":\"USDC\"");
        raw.Should().NotContainAny("\"positions\"", "staleAfter", "snapshotPositionIds");
        store.VerifyAll();
    }

    [Fact]
    public async Task Get_returns_no_content_when_owned_account_has_no_snapshot()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var store = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        store.Setup(x => x.GetLatestAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(true, null));
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        store.VerifyAll();
    }

    [Fact]
    public async Task Get_hides_missing_and_foreign_accounts_as_the_same_not_found_problem()
    {
        var userId = UserId.New();
        var store = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        store.Setup(x => x.GetLatestAsync(
                userId,
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(false, null));
        using var client = CreateClient(userId, store.Object);

        using var missingResponse = await client.GetAsync(
            $"/api/v1/exchange-accounts/{ExchangeAccountId.New().Value}/portfolio");
        using var foreignResponse = await client.GetAsync(
            $"/api/v1/exchange-accounts/{ExchangeAccountId.New().Value}/portfolio");

        var missing = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var foreign = await foreignResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreign!.Type.Should().Be(missing!.Type);
        foreign.Title.Should().Be(missing.Title);
        foreign.Detail.Should().Be(missing.Detail);
        foreign.Extensions["code"]!.ToString().Should().Be("resource_not_found");
        store.Verify(
            x => x.GetLatestAsync(
                userId,
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Get_rejects_empty_account_id_without_calling_application()
    {
        var store = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        using var client = CreateClient(UserId.New(), store.Object);

        using var response = await client.GetAsync(
            $"/api/v1/exchange-accounts/{Guid.Empty}/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
        store.Verify(
            x => x.GetLatestAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_rejects_malformed_account_guid()
    {
        var store = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        using var client = CreateClient(UserId.New(), store.Object);

        using var response = await client.GetAsync(
            "/api/v1/exchange-accounts/not-a-guid/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
        store.VerifyNoOtherCalls();
    }

    private HttpClient CreateClient(
        UserId userId,
        IPortfolioReadStore store,
        IReadOnlyList<PositionMarketValuationInput>? positions = null)
    {
        var observationStore = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        observationStore.Setup(x => x.GetAsync(
                userId,
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<RealtimeObservationState>?)null);
        var valuationInputs = positions ?? [];
        var positionStore = new Mock<IPositionReadStore>(MockBehavior.Strict);
        positionStore.Setup(x => x.GetMarketValuationInputsByAccountAsync(
                userId,
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuationInputs);
        var readService = new PortfolioReadService(
            store,
            new RealtimeObservationReadService(
                observationStore.Object,
                new RealtimeObservationReadOptions
                {
                    HeartbeatStaleAfter = TimeSpan.FromMinutes(1),
                },
                TimeProvider.System),
            positionStore.Object,
            new MarketMarkPriceService(Mock.Of<IMarketMarkPriceStore>(), TimeProvider.System),
            TimeProvider.System);
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PortfolioReadService>();
                services.AddSingleton(readService);
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
}
