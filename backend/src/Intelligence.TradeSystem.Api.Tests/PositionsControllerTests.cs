using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using DomainPositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PositionsControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task List_maps_typed_query_and_returns_cursor_page()
    {
        var userId = UserId.New();
        var position = CreateListItem();
        var nextCursor = new PositionReadCursor(position.FirstDetectedAt, position.Id);
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        PositionReadQuery? capturedQuery = null;
        store.Setup(x => x.ListAsync(userId, It.IsAny<PositionReadQuery>(), It.IsAny<CancellationToken>()))
            .Callback<UserId, PositionReadQuery, CancellationToken>((_, query, _) => capturedQuery = query)
            .ReturnsAsync(new PositionReadPage([position], nextCursor, true));
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync(
            "/api/v1/positions?symbol=%20btcusdt%20&trackingState=closed&side=long&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CursorPage<PositionListItemResponse>>(
            V1JsonSerializerOptions.Default);
        body!.Items.Should().ContainSingle();
        body.Items[0].Symbol.Should().Be("BTCUSDT");
        body.Items[0].Side.Should().Be(PositionSideV1.Long);
        body.Items[0].TrackingState.Should().Be(PositionTrackingStateV1.Closed);
        body.NextCursor.Should().NotBeNullOrWhiteSpace();
        body.HasMore.Should().BeTrue();
        PositionCursorCodec.TryDecode(body.NextCursor!, out var decoded).Should().BeTrue();
        decoded.Should().Be(nextCursor);

        capturedQuery.Should().NotBeNull();
        capturedQuery!.TrackingStates.Should().Equal(PositionTrackingState.Closed);
        capturedQuery.Symbol.Should().Be("btcusdt");
        capturedQuery.Side.Should().Be(DomainPositionSide.Long);
        capturedQuery.PageSize.Should().Be(2);
        store.VerifyAll();
    }

    [Fact]
    public async Task List_uses_active_unknown_and_stale_by_default()
    {
        var userId = UserId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        PositionReadQuery? capturedQuery = null;
        store.Setup(x => x.ListAsync(userId, It.IsAny<PositionReadQuery>(), It.IsAny<CancellationToken>()))
            .Callback<UserId, PositionReadQuery, CancellationToken>((_, query, _) => capturedQuery = query)
            .ReturnsAsync(new PositionReadPage([], null, false));
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync("/api/v1/positions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedQuery!.TrackingStates.Should().Equal(
            PositionTrackingState.Active,
            PositionTrackingState.Unknown,
            PositionTrackingState.Stale);
        capturedQuery.PageSize.Should().Be(CursorPagination.DefaultPageSize);
    }

    [Theory]
    [InlineData("trackingState=ACTIVE")]
    [InlineData("trackingState=other")]
    [InlineData("trackingState=active&trackingState=closed")]
    [InlineData("side=unknown")]
    [InlineData("exchangeAccountId=not-a-guid")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("symbol=%20%20")]
    [InlineData("cursor=invalid")]
    public async Task List_rejects_invalid_wire_values_without_calling_application(
        string query)
    {
        var userId = UserId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync($"/api/v1/positions?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
        store.Verify(
            x => x.ListAsync(
                It.IsAny<UserId>(),
                It.IsAny<PositionReadQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Detail_hides_missing_and_foreign_positions_as_the_same_not_found_problem()
    {
        var userId = UserId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        store.Setup(x => x.GetByIdAsync(
                userId,
                It.IsAny<PositionId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionReadDetail?)null);
        using var client = CreateClient(userId, store.Object);

        using var missingResponse = await client.GetAsync($"/api/v1/positions/{Guid.NewGuid()}");
        using var foreignResponse = await client.GetAsync($"/api/v1/positions/{Guid.NewGuid()}");

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
    public async Task Detail_returns_current_state_without_history_or_analysis_fields()
    {
        var userId = UserId.New();
        var detail = CreateDetail();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        store.Setup(x => x.GetByIdAsync(
                userId,
                detail.ListItem.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync($"/api/v1/positions/{detail.ListItem.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("\"marketCategory\":\"linear\"");
        raw.Should().NotContainAny("positionIdx", "changes", "timeline", "assessment", "recommendation");
    }

    [Fact]
    public async Task Detail_rejects_malformed_guid_as_validation_problem()
    {
        var userId = UserId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync("/api/v1/positions/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Portfolio_returns_summary_without_embedded_positions()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        portfolioStore.Setup(x => x.GetLatestAsync(userId, accountId, It.IsAny<CancellationToken>()))
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
                true)));
        using var client = CreateClient(userId, store.Object, portfolioStore.Object);

        using var response = await client.GetAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(
            V1JsonSerializerOptions.Default);
        body!.ExchangeAccountId.Should().Be(accountId.Value);
        body.Capital.TotalEquity.Should().Be(1000m);
        body.LargestPositionId.Should().BeNull();
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny("\"positions\"", "staleAfter");
    }

    [Fact]
    public async Task Portfolio_returns_no_content_when_owned_account_has_no_snapshot()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        portfolioStore.Setup(x => x.GetLatestAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(true, null));
        using var client = CreateClient(userId, store.Object, portfolioStore.Object);

        using var response = await client.GetAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Portfolio_hides_missing_and_foreign_accounts()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        portfolioStore.Setup(x => x.GetLatestAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortfolioReadResult(false, null));
        using var client = CreateClient(userId, store.Object, portfolioStore.Object);

        using var response = await client.GetAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("resource_not_found");
    }

    [Fact]
    public async Task Portfolio_rejects_malformed_account_guid()
    {
        var userId = UserId.New();
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        var portfolioStore = new Mock<IPortfolioReadStore>(MockBehavior.Strict);
        using var client = CreateClient(userId, store.Object, portfolioStore.Object);

        using var response = await client.GetAsync(
            "/api/v1/exchange-accounts/not-a-guid/portfolio");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
    }

    private HttpClient CreateClient(
        UserId userId,
        IPositionReadStore positionStore,
        IPortfolioReadStore? portfolioStore = null)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PositionReadService>();
                services.AddSingleton(new PositionReadService(positionStore));
                services.RemoveAll<PortfolioReadService>();
                services.AddSingleton(new PortfolioReadService(
                    portfolioStore ?? new Mock<IPortfolioReadStore>(MockBehavior.Strict).Object));
                services.RemoveAll<IExchangeAccountService>();
                services.AddSingleton(new Mock<IExchangeAccountService>(MockBehavior.Strict).Object);
                services.RemoveAll<IExchangeAccountSyncService>();
                services.AddSingleton(new Mock<IExchangeAccountSyncService>(MockBehavior.Strict).Object);
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

    private static PositionReadListItem CreateListItem()
    {
        var positionId = PositionId.New();
        return new PositionReadListItem(
            positionId,
            ExchangeAccountId.New(),
            "BTCUSDT",
            DomainPositionSide.Long,
            PositionTrackingState.Closed,
            1m,
            100m,
            110m,
            110m,
            10m,
            2m,
            50m,
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 20, 10, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 20, 10, 5, 0, TimeSpan.Zero));
    }

    private static PositionReadDetail CreateDetail()
    {
        var listItem = CreateListItem() with
        {
            TrackingState = PositionTrackingState.Active,
            ClosedAt = null,
        };
        return new PositionReadDetail(
            listItem,
            MarketCategory.Linear,
            101m,
            130m,
            90m,
            2m);
    }
}
