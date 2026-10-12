using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class PositionEndpointsTests
{
    private const string AccountId = "2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6";
    private const string ApiPositions = BffApplicationFactory.ApiBaseAddress + "api/v1/positions";
    private const string PositionId = "8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001";
    private const string PositionPath = "/bff/me/positions/" + PositionId;
    private const string ApiPosition = ApiPositions + "/" + PositionId;

    private const string ResourceJson =
        """{"positionId":"8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001","symbol":"BTCUSDT","settlementAsset":"USDT"}""";

    private const string TimelineJson =
        """{"items":[{"type":"evaluation","occurredAt":"2026-10-12T10:00:00Z","positionChange":null,"evaluation":{"id":"e1"},"recommendation":null}],"nextCursor":"opaque+cursor/==","hasMore":true}""";

    private const string PageJson =
        """{"items":[{"id":"8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001","symbol":"BTCUSDT","settlementAsset":"USDT"}],"nextCursor":"opaque+cursor/=="}""";

    [Fact]
    public async Task List_forwards_to_the_exact_api_route_with_bearer_and_preserves_the_response()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "access-visible-only-to-bff");

        using var response = await browser.Client.GetAsync("/bff/me/positions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(PageJson);
        body.Should().NotContain("access-visible-only-to-bff").And.NotContain("refresh-1");
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Get);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(ApiPositions);
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-visible-only-to-bff");
    }

    [Fact]
    public async Task Allowlisted_query_is_forwarded_url_encoded_and_unknown_parameters_are_dropped()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        var cursor = Uri.EscapeDataString("opaque+cursor/==&userId=other");
        using var response = await browser.Client.GetAsync(
            "/bff/me/positions?userId=other&exchangeAccountId=" + AccountId
            + "&trackingState=active&symbol=BTC%20USDT&side=long&pageSize=25&cursor=" + cursor
            + "&path=..%2Fauth%2Fme&includeClosed=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.RequestUri.AbsolutePath.Should().Be("/api/v1/positions");
        var query = ParseQuery(apiRequest.RequestUri);
        query.Select(pair => pair.Key).Should().Equal(
            "exchangeAccountId", "trackingState", "symbol", "side", "pageSize", "cursor");
        query.Should().Contain(new KeyValuePair<string, string>("exchangeAccountId", AccountId));
        query.Should().Contain(new KeyValuePair<string, string>("trackingState", "active"));
        query.Should().Contain(new KeyValuePair<string, string>("symbol", "BTC USDT"));
        query.Should().Contain(new KeyValuePair<string, string>("side", "long"));
        query.Should().Contain(new KeyValuePair<string, string>("pageSize", "25"));
        query.Should().Contain(new KeyValuePair<string, string>("cursor", "opaque+cursor/==&userId=other"));
        apiRequest.RequestUri.Query.Should().NotContain("path").And.NotContain("includeClosed");
    }

    [Fact]
    public async Task Repeated_and_empty_allowlisted_values_are_forwarded_for_api_validation()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync(
            "/bff/me/positions?trackingState=active&trackingState=closed&exchangeAccountId=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = ParseQuery(factory.Api.Requests.Should().ContainSingle().Subject.RequestUri);
        query.Should().Equal(
            new KeyValuePair<string, string>("exchangeAccountId", string.Empty),
            new KeyValuePair<string, string>("trackingState", "active"),
            new KeyValuePair<string, string>("trackingState", "closed"));
    }

    [Fact]
    public async Task Api_validation_problem_is_preserved_without_reclassification()
    {
        const string problem =
            """{"type":"urn:intelligence-trade:error:validation","title":"Title","status":400,"code":"validation_failed","traceId":"00-trace-01","errors":{"cursor":["invalid"]}}""";
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json"),
        });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync("/bff/me/positions?cursor=broken");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Be(problem);
        factory.Api.Count.Should().Be(1);
    }

    [Fact]
    public async Task Browser_authorization_header_is_not_forwarded_to_the_api()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/me/positions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-supplied-token");

        using var response = await browser.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Api.Requests.Should().ContainSingle()
            .Which.AuthorizationParameter.Should().Be("access-1");
    }

    [Fact]
    public async Task Api_unauthorized_triggers_one_forced_refresh_and_one_retry_with_the_same_query()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (request, _) => Task.FromResult(request.AuthorizationParameter == "access-2"
            ? RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync("/bff/me/positions?trackingState=closed");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Requests.Select(request => request.AuthorizationParameter)
            .Should().Equal("access-1", "access-2");
        factory.Api.Requests.Should().OnlyContain(
            request => request.RequestUri.AbsoluteUri == ApiPositions + "?trackingState=closed");
    }

    [Fact]
    public async Task Repeated_api_unauthorized_ends_the_session_and_returns_unauthorized()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync("/bff/me/positions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(2);
        factory.TokenEndpoint.Count.Should().Be(1);
        browser.SessionCookie.Should().BeNull();
    }

    [Fact]
    public async Task Anonymous_browser_receives_unauthorized_without_api_calls()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync("/bff/me/positions?userId=other");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(PositionPath, ApiPosition)]
    [InlineData(PositionPath + "/market", ApiPosition + "/market")]
    [InlineData(PositionPath + "/evaluation", ApiPosition + "/evaluation")]
    [InlineData(PositionPath + "/timeline", ApiPosition + "/timeline")]
    public async Task Position_reads_forward_to_the_exact_api_route_without_query(string path, string apiUri)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, ResourceJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "access-visible-only-to-bff");

        using var response = await browser.Client.GetAsync(path + "?userId=other&path=..%2F..%2Fauth%2Fme&interval=1h");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(ResourceJson);
        body.Should().NotContain("access-visible-only-to-bff").And.NotContain("refresh-1");
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Get);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(apiUri);
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-visible-only-to-bff");
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Candles_forward_only_interval_and_limit_url_encoded()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, ResourceJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync(
            PositionPath + "/candles?userId=other&limit=200&from=0&interval=1h%26userId%3Dother&path=..%2Fmarket");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(ResourceJson);
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Get);
        apiRequest.RequestUri.AbsolutePath.Should().Be("/api/v1/positions/" + PositionId + "/candles");
        ParseQuery(apiRequest.RequestUri).Should().Equal(
            new KeyValuePair<string, string>("interval", "1h&userId=other"),
            new KeyValuePair<string, string>("limit", "200"));
    }

    [Fact]
    public async Task Candles_repeated_and_missing_values_are_left_for_api_validation()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, ResourceJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var withoutQuery = await browser.Client.GetAsync(PositionPath + "/candles");
        using var repeated = await browser.Client.GetAsync(PositionPath + "/candles?interval=1h&interval=4h&limit=");

        withoutQuery.StatusCode.Should().Be(HttpStatusCode.OK);
        repeated.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Api.Requests.Should().HaveCount(2);
        factory.Api.Requests[0].RequestUri.AbsoluteUri.Should().Be(ApiPosition + "/candles");
        ParseQuery(factory.Api.Requests[1].RequestUri).Should().Equal(
            new KeyValuePair<string, string>("interval", "1h"),
            new KeyValuePair<string, string>("interval", "4h"),
            new KeyValuePair<string, string>("limit", string.Empty));
    }

    [Fact]
    public async Task Timeline_forwards_only_page_size_repeated_type_and_opaque_cursor_url_encoded()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, TimelineJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "access-visible-only-to-bff");

        var cursor = Uri.EscapeDataString("opaque+cursor/==&userId=other%2F");
        using var response = await browser.Client.GetAsync(
            PositionPath + "/timeline?userId=other&cursor=" + cursor
            + "&type=positionChange&exchangeAccountId=" + AccountId
            + "&type=evaluation&pageSize=25&limit=10&interval=1h&path=..%2Fevaluation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(TimelineJson);
        body.Should().NotContain("access-visible-only-to-bff").And.NotContain("refresh-1");
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Get);
        apiRequest.RequestUri.AbsolutePath.Should().Be("/api/v1/positions/" + PositionId + "/timeline");
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-visible-only-to-bff");
        ParseQuery(apiRequest.RequestUri).Should().Equal(
            new KeyValuePair<string, string>("pageSize", "25"),
            new KeyValuePair<string, string>("type", "positionChange"),
            new KeyValuePair<string, string>("type", "evaluation"),
            new KeyValuePair<string, string>("cursor", "opaque+cursor/==&userId=other%2F"));
    }

    [Fact]
    public async Task Timeline_repeated_and_empty_values_are_left_for_api_validation()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, TimelineJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync(
            PositionPath + "/timeline?type=recommendation&type=&type=recommendation&pageSize=&pageSize=500&cursor=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ParseQuery(factory.Api.Requests.Should().ContainSingle().Subject.RequestUri).Should().Equal(
            new KeyValuePair<string, string>("pageSize", string.Empty),
            new KeyValuePair<string, string>("pageSize", "500"),
            new KeyValuePair<string, string>("type", "recommendation"),
            new KeyValuePair<string, string>("type", string.Empty),
            new KeyValuePair<string, string>("type", "recommendation"),
            new KeyValuePair<string, string>("cursor", string.Empty));
    }

    [Fact]
    public async Task Evaluate_forwards_to_the_exact_api_route_with_bearer_and_without_body()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, ResourceJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(
            browser,
            "POST",
            PositionPath + "/evaluation?userId=other",
            antiforgery,
            body: "{\"userId\":\"other\"}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Be(ResourceJson);
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Post);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(ApiPosition + "/evaluation");
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-1");
        apiRequest.Body.Should().BeEmpty();
        factory.RequestBody.ReadCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forged-token")]
    public async Task Evaluate_without_valid_antiforgery_token_is_rejected_before_the_api(string? antiforgery)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, ResourceJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await SendAsync(browser, "POST", PositionPath + "/evaluation", antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Api.Count.Should().Be(0);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Evaluation_not_yet_performed_is_preserved_as_no_content()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync(PositionPath + "/evaluation");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        factory.Api.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("GET", PositionPath, 404, "resource_not_found")]
    [InlineData("GET", PositionPath + "/market", 404, "resource_not_found")]
    [InlineData("GET", PositionPath + "/market", 503, "market_data_unavailable")]
    [InlineData("GET", PositionPath + "/candles?interval=1h", 400, "validation_failed")]
    [InlineData("GET", PositionPath + "/candles?interval=1h", 503, "exchange_unavailable")]
    [InlineData("GET", PositionPath + "/evaluation", 404, "resource_not_found")]
    [InlineData("GET", PositionPath + "/timeline?cursor=broken", 400, "validation_failed")]
    [InlineData("GET", PositionPath + "/timeline", 404, "resource_not_found")]
    [InlineData("POST", PositionPath + "/evaluation", 404, "resource_not_found")]
    [InlineData("POST", PositionPath + "/evaluation", 503, "market_data_unavailable")]
    public async Task Position_api_problem_details_are_preserved_without_reclassification(
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
    public async Task Evaluate_conflict_with_reason_is_preserved_without_retry()
    {
        const string problem =
            """{"type":"urn:intelligence-trade:error:position-not-evaluable","title":"Title","status":409,"code":"position_not_evaluable","reason":"closedPosition","traceId":"00-trace-01"}""";
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json"),
        });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", PositionPath + "/evaluation", antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Be(problem);
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", PositionPath, ApiPosition)]
    [InlineData("GET", PositionPath + "/market", ApiPosition + "/market")]
    [InlineData("GET", PositionPath + "/candles?interval=4h&limit=200", ApiPosition + "/candles?interval=4h&limit=200")]
    [InlineData("GET", PositionPath + "/evaluation", ApiPosition + "/evaluation")]
    [InlineData(
        "GET",
        PositionPath + "/timeline?pageSize=25&type=evaluation&cursor=c1",
        ApiPosition + "/timeline?pageSize=25&type=evaluation&cursor=c1")]
    [InlineData("POST", PositionPath + "/evaluation", ApiPosition + "/evaluation")]
    public async Task Position_operations_retry_once_after_forced_refresh_on_api_unauthorized(
        string method,
        string path,
        string apiUri)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (request, _) => Task.FromResult(request.AuthorizationParameter == "access-2"
            ? RecordingHttpHandler.Json(HttpStatusCode.OK, ResourceJson)
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
        factory.Api.Requests.Should().OnlyContain(request => request.RequestUri.AbsoluteUri == apiUri);
    }

    [Theory]
    [InlineData("GET", PositionPath)]
    [InlineData("GET", PositionPath + "/timeline")]
    [InlineData("POST", PositionPath + "/evaluation")]
    public async Task Repeated_api_unauthorized_on_position_operations_ends_the_session(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(2);
        factory.TokenEndpoint.Count.Should().Be(1);
        browser.SessionCookie.Should().BeNull();
    }

    [Theory]
    [InlineData(PositionPath)]
    [InlineData(PositionPath + "/market")]
    [InlineData(PositionPath + "/candles?interval=1h")]
    [InlineData(PositionPath + "/evaluation")]
    [InlineData(PositionPath + "/timeline?type=evaluation&userId=other")]
    public async Task Anonymous_browser_receives_unauthorized_for_position_reads_without_api_calls(string path)
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", PositionPath + "/market")]
    [InlineData("GET", PositionPath + "/timeline")]
    [InlineData("POST", PositionPath + "/evaluation")]
    public async Task Network_failure_is_not_retried_and_does_not_leak_exception_text(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => throw new HttpRequestException("connection refused: internal-api-host:8080");
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("connection refused").And.NotContain("internal-api-host");
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", PositionPath + "/candles?interval=1h")]
    [InlineData("GET", PositionPath + "/timeline?type=recommendation")]
    [InlineData("POST", PositionPath + "/evaluation")]
    public async Task Timeout_is_not_retried(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) =>
            throw new TaskCanceledException("upstream timeout", new TimeoutException());
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", "/bff/me/positions", HttpStatusCode.ServiceUnavailable)]
    [InlineData("GET", PositionPath, HttpStatusCode.InternalServerError)]
    [InlineData("GET", PositionPath + "/evaluation", HttpStatusCode.BadGateway)]
    [InlineData("GET", PositionPath + "/timeline", HttpStatusCode.InternalServerError)]
    [InlineData("GET", PositionPath + "/timeline", HttpStatusCode.ServiceUnavailable)]
    [InlineData("POST", PositionPath + "/evaluation", HttpStatusCode.InternalServerError)]
    [InlineData("POST", PositionPath + "/evaluation", HttpStatusCode.ServiceUnavailable)]
    [InlineData("POST", PositionPath + "/evaluation", HttpStatusCode.GatewayTimeout)]
    public async Task Server_errors_are_not_retried(string method, string path, HttpStatusCode status)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(status));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, antiforgery);

        response.StatusCode.Should().Be(status);
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("POST", "/bff/me/positions")]
    [InlineData("GET", "/bff/me/positions/extra/path")]
    [InlineData("GET", "/bff/api/v1/positions")]
    [InlineData("GET", "/bff/me/positions/not-a-guid")]
    [InlineData("GET", "/bff/me/positions/not-a-guid/market")]
    [InlineData("POST", "/bff/me/positions/not-a-guid/evaluation")]
    [InlineData("GET", "/bff/me/positions/not-a-guid/timeline")]
    [InlineData("GET", PositionPath + "/extra")]
    [InlineData("GET", PositionPath + "/market/extra")]
    [InlineData("GET", PositionPath + "/evaluation/extra")]
    [InlineData("GET", PositionPath + "/timeline/extra")]
    [InlineData("GET", PositionPath + "/history")]
    [InlineData("GET", PositionPath + "/sync")]
    [InlineData("POST", PositionPath)]
    [InlineData("PUT", PositionPath)]
    [InlineData("DELETE", PositionPath)]
    [InlineData("POST", PositionPath + "/market")]
    [InlineData("POST", PositionPath + "/candles")]
    [InlineData("PUT", PositionPath + "/evaluation")]
    [InlineData("DELETE", PositionPath + "/evaluation")]
    [InlineData("POST", PositionPath + "/timeline")]
    [InlineData("PUT", PositionPath + "/timeline")]
    [InlineData("DELETE", PositionPath + "/timeline")]
    public async Task Unknown_position_paths_return_not_found_without_reaching_the_api(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson));
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

    private static List<KeyValuePair<string, string>> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(pair => new KeyValuePair<string, string>(
                Uri.UnescapeDataString(pair[0]),
                pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty))
            .ToList();
}
