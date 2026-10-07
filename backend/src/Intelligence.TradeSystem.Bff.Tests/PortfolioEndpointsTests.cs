using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class PortfolioEndpointsTests
{
    private const string AccountId = "2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6";
    private const string PortfolioPath = "/bff/me/exchange-accounts/" + AccountId + "/portfolio";
    private const string SyncPath = "/bff/me/exchange-accounts/" + AccountId + "/sync";
    private const string ApiPortfolio = BffApplicationFactory.ApiBaseAddress + "api/v1/exchange-accounts/" + AccountId + "/portfolio";
    private const string ApiSync = BffApplicationFactory.ApiBaseAddress + "api/v1/exchange-accounts/" + AccountId + "/sync";

    private const string PortfolioJson =
        """{"exchangeAccountId":"2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6","totalEquity":1000.5,"currentPositionCount":2,"exposures":[{"settlementAsset":"USDT","grossExposure":10,"longExposure":10,"shortExposure":0}]}""";

    private const string AccountJson =
        """{"id":"2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6","displayName":"Основной","exchange":"bybit","connectionStatus":"connected","capabilities":["readBalance","readPositions"],"lastSyncedAt":"2026-10-07T10:00:00+00:00"}""";

    [Fact]
    public async Task Portfolio_forwards_to_the_exact_api_route_with_bearer_and_preserves_the_response()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PortfolioJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "access-visible-only-to-bff");

        using var response = await browser.Client.GetAsync(PortfolioPath + "?userId=other&path=../positions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(PortfolioJson);
        body.Should().NotContain("access-visible-only-to-bff").And.NotContain("refresh-1");
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Get);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(ApiPortfolio);
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-visible-only-to-bff");
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Portfolio_no_content_is_preserved_without_body()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync(PortfolioPath);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        factory.Api.Count.Should().Be(1);
    }

    [Fact]
    public async Task Browser_authorization_header_is_not_forwarded_to_the_api()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PortfolioJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, PortfolioPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-supplied-token");

        using var response = await browser.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Api.Requests.Should().ContainSingle()
            .Which.AuthorizationParameter.Should().Be("access-1");
    }

    [Theory]
    [InlineData("GET", PortfolioPath, 404, "resource_not_found")]
    [InlineData("POST", SyncPath, 404, "resource_not_found")]
    [InlineData("POST", SyncPath, 409, "exchange_account_disabled")]
    [InlineData("POST", SyncPath, 503, "exchange_unavailable")]
    public async Task Api_problem_details_are_preserved_without_reclassification(
        string method,
        string path,
        int status,
        string code)
    {
        var problem =
            $$$"""{"type":"urn:intelligence-trade:error:test","title":"Title","status":{{{status}}},"code":"{{{code}}}","traceId":"00-trace-01"}""";
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json"),
        });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be((HttpStatusCode)status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Be(problem);
        factory.Api.Count.Should().Be(1);
    }

    [Fact]
    public async Task Sync_forwards_to_the_exact_api_route_with_bearer_and_without_body()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, AccountJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", SyncPath, antiforgery, body: "{\"userId\":\"other\"}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Be(AccountJson);
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Post);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(ApiSync);
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-1");
        apiRequest.Body.Should().BeEmpty();
        factory.RequestBody.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task Sync_without_antiforgery_token_is_rejected_before_the_api()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, AccountJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await SendAsync(browser, "POST", SyncPath, antiforgery: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Api.Count.Should().Be(0);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", PortfolioPath)]
    [InlineData("POST", SyncPath)]
    public async Task Api_unauthorized_triggers_one_forced_refresh_and_one_retry(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (request, _) => Task.FromResult(request.AuthorizationParameter == "access-2"
            ? RecordingHttpHandler.Json(HttpStatusCode.OK, method == "GET" ? PortfolioJson : AccountJson)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("access-2").And.NotContain("refresh-2");
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Requests.Select(request => request.AuthorizationParameter)
            .Should().Equal("access-1", "access-2");
        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }

    [Theory]
    [InlineData("GET", PortfolioPath)]
    [InlineData("POST", SyncPath)]
    public async Task Repeated_api_unauthorized_ends_the_session_and_returns_unauthorized(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        var sessionKey = browser.GetSessionKey();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(2);
        factory.TokenEndpoint.Count.Should().Be(1);
        browser.SessionCookie.Should().BeNull();
        (await factory.Services.GetTicketStore().RetrieveAsync(sessionKey!)).Should().BeNull();
    }

    [Fact]
    public async Task Anonymous_browser_receives_unauthorized_without_api_calls()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync(PortfolioPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Sync_network_failure_is_not_retried_and_does_not_leak_exception_text()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => throw new HttpRequestException("connection refused: internal-api-host:8080");
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", SyncPath, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("connection refused").And.NotContain("internal-api-host");
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Sync_timeout_is_not_retried()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) =>
            throw new TaskCanceledException("upstream timeout", new TimeoutException());
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", SyncPath, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Sync_server_errors_are_not_retried(HttpStatusCode status)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(status));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", SyncPath, antiforgery);

        response.StatusCode.Should().Be(status);
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", "/bff/me/exchange-accounts/not-a-guid/portfolio")]
    [InlineData("POST", "/bff/me/exchange-accounts/not-a-guid/sync")]
    [InlineData("GET", "/bff/me/exchange-accounts/" + AccountId + "/portfolio/extra")]
    [InlineData("POST", "/bff/me/exchange-accounts/" + AccountId + "/sync/extra")]
    [InlineData("GET", "/bff/me/exchange-accounts/" + AccountId + "/sync")]
    [InlineData("PUT", "/bff/me/exchange-accounts/" + AccountId + "/portfolio")]
    [InlineData("GET", "/bff/api/v1/exchange-accounts/" + AccountId + "/portfolio")]
    [InlineData("GET", "/bff/me/exchange-accounts/" + AccountId + "/positions")]
    public async Task Unknown_portfolio_and_sync_paths_return_not_found_without_reaching_the_api(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PortfolioJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        BffBrowser browser,
        string method,
        string path,
        string? antiforgery,
        string? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (antiforgery is not null)
        {
            request.Headers.Add(BffAuthenticationExtensions.AntiforgeryHeaderName, antiforgery);
        }

        return await browser.Client.SendAsync(request);
    }
}
