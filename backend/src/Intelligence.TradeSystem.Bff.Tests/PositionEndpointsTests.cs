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
    [InlineData("POST", "/bff/me/positions")]
    [InlineData("GET", "/bff/me/positions/8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001")]
    [InlineData("GET", "/bff/me/positions/extra/path")]
    [InlineData("GET", "/bff/api/v1/positions")]
    public async Task Unknown_position_paths_return_not_found_without_reaching_the_api(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, PageJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(BffAuthenticationExtensions.AntiforgeryHeaderName, antiforgery);

        using var response = await browser.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
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
