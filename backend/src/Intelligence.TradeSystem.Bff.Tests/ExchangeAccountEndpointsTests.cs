using System.Net;
using System.Text;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class ExchangeAccountEndpointsTests
{
    private const string AccountId = "2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6";
    private const string ApiCollection = BffApplicationFactory.ApiBaseAddress + "api/v1/me/exchange-accounts";
    private const string FakeApiKey = "fake-api-key-not-a-real-bybit-key";
    private const string FakeApiSecret = "fake-api-secret-not-a-real-bybit-secret";

    private const string AccountJson =
        """{"id":"2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6","displayName":"Основной","exchange":"bybit","connectionStatus":"connected","capabilities":["readBalance","readPositions"],"lastSyncedAt":null}""";

    private static readonly string CreateBody =
        $$"""{"displayName":"Основной","exchange":"bybit","apiKey":"{{FakeApiKey}}","apiSecret":"{{FakeApiSecret}}"}""";

    private static readonly string RotateBody =
        $$"""{"apiKey":"{{FakeApiKey}}","apiSecret":"{{FakeApiSecret}}"}""";

    [Fact]
    public async Task List_forwards_to_the_exact_api_route_with_bearer_and_preserves_the_response()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(
            RecordingHttpHandler.Json(HttpStatusCode.OK, "{\"items\":[" + AccountJson + "]}"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "access-visible-only-to-bff");

        using var response = await browser.Client.GetAsync("/bff/me/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("{\"items\":[" + AccountJson + "]}");
        body.Should().NotContain("access-visible-only-to-bff").And.NotContain("refresh-1");
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Should().Be(HttpMethod.Get);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(ApiCollection);
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-visible-only-to-bff");
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Each_operation_forwards_to_its_predefined_api_route_and_body(
        string method,
        string bffPath,
        string? requestBody,
        string expectedApiPath,
        int apiStatus)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(apiStatus == 204
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : RecordingHttpHandler.Json((HttpStatusCode)apiStatus, AccountJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, bffPath, requestBody, antiforgery);

        response.StatusCode.Should().Be((HttpStatusCode)apiStatus);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var responseBody = await response.Content.ReadAsStringAsync();
        if (apiStatus == 204)
        {
            responseBody.Should().BeEmpty();
        }
        else
        {
            responseBody.Should().Be(AccountJson);
        }

        responseBody.Should().NotContain(FakeApiKey).And.NotContain(FakeApiSecret).And.NotContain("access-1");
        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.Method.Method.Should().Be(method);
        apiRequest.RequestUri.AbsoluteUri.Should().Be(BffApplicationFactory.ApiBaseAddress + expectedApiPath);
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-1");
        apiRequest.Body.Should().Be(requestBody ?? string.Empty);
    }

    public static TheoryData<string, string, string?, string, int> Operations() => new()
    {
        { "POST", "/bff/me/exchange-accounts", CreateBody, "api/v1/me/exchange-accounts", 201 },
        { "POST", "/bff/me/exchange-accounts", CreateBody, "api/v1/me/exchange-accounts", 200 },
        {
            "PATCH", $"/bff/me/exchange-accounts/{AccountId}", "{\"displayName\":\"GinArea\"}",
            $"api/v1/me/exchange-accounts/{AccountId}", 200
        },
        {
            "POST", $"/bff/me/exchange-accounts/{AccountId}/verify", null,
            $"api/v1/me/exchange-accounts/{AccountId}/verify", 200
        },
        {
            "PUT", $"/bff/me/exchange-accounts/{AccountId}/credentials", RotateBody,
            $"api/v1/me/exchange-accounts/{AccountId}/credentials", 200
        },
        { "DELETE", $"/bff/me/exchange-accounts/{AccountId}", null, $"api/v1/me/exchange-accounts/{AccountId}", 204 },
    };

    [Theory]
    [InlineData(400, "validation_failed")]
    [InlineData(403, "exchange_permissions_rejected")]
    [InlineData(404, "resource_not_found")]
    [InlineData(409, "exchange_account_already_exists")]
    [InlineData(503, "exchange_unavailable")]
    public async Task Api_problem_details_are_preserved_without_reclassification(int status, string code)
    {
        var problem =
            $$$"""{"type":"urn:intelligence-trade:error:test","title":"Title","status":{{{status}}},"code":"{{{code}}}","traceId":"00-trace-01","errors":{"displayName":["invalid"]}}""";
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json"),
        });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", "/bff/me/exchange-accounts", CreateBody, antiforgery);

        response.StatusCode.Should().Be((HttpStatusCode)status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(problem);
        body.Should().NotContain(FakeApiKey).And.NotContain(FakeApiSecret);
        factory.Api.Count.Should().Be(1);
    }

    [Fact]
    public async Task Api_unauthorized_triggers_one_forced_refresh_and_one_retry_with_the_same_body()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (request, _) => Task.FromResult(request.AuthorizationParameter == "access-2"
            ? RecordingHttpHandler.Json(HttpStatusCode.Created, AccountJson)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", "/bff/me/exchange-accounts", CreateBody, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("access-2").And.NotContain("refresh-2");
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Requests.Select(request => request.AuthorizationParameter)
            .Should().Equal("access-1", "access-2");
        factory.Api.Requests.Should().OnlyContain(request => request.Body == CreateBody);
        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
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
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        var sessionKey = browser.GetSessionKey();

        using var response = await SendAsync(
            browser, "PATCH", $"/bff/me/exchange-accounts/{AccountId}", "{\"displayName\":\"GinArea\"}", antiforgery);

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

        using var response = await browser.Client.GetAsync("/bff/me/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Rejected_refresh_ends_the_session_and_returns_unauthorized_without_api_calls()
    {
        using var factory = new BffApplicationFactory();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenError(HttpStatusCode.BadRequest, "invalid_grant"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));

        using var response = await browser.Client.GetAsync("/bff/me/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Api.Count.Should().Be(0);
        browser.SessionCookie.Should().BeNull();
    }

    [Fact]
    public async Task Unavailable_token_service_returns_service_unavailable_and_keeps_the_session()
    {
        using var factory = new BffApplicationFactory();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));

        using var response = await browser.Client.GetAsync("/bff/me/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        factory.Api.Count.Should().Be(0);
        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Network_failure_of_an_unsafe_request_is_not_retried_and_does_not_leak_exception_text()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => throw new HttpRequestException("connection refused: internal-api-host:8080");
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, "POST", "/bff/me/exchange-accounts", CreateBody, antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("connection refused").And.NotContain("internal-api-host");
        body.Should().NotContain(FakeApiKey).And.NotContain(FakeApiSecret);
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("POST", "/bff/me/exchange-accounts", HttpStatusCode.InternalServerError)]
    [InlineData("PUT", "/bff/me/exchange-accounts/" + AccountId + "/credentials", HttpStatusCode.ServiceUnavailable)]
    [InlineData("DELETE", "/bff/me/exchange-accounts/" + AccountId, HttpStatusCode.BadGateway)]
    [InlineData("PATCH", "/bff/me/exchange-accounts/" + AccountId, HttpStatusCode.GatewayTimeout)]
    public async Task Server_errors_of_unsafe_requests_are_not_retried(string method, string path, HttpStatusCode status)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(status));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, method is "DELETE" ? null : RotateBody, antiforgery);

        response.StatusCode.Should().Be(status);
        factory.Api.Count.Should().Be(1);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Non_json_api_body_is_not_forwarded_to_the_browser()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>internal proxy page</html>", Encoding.UTF8, "text/html"),
        });
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync("/bff/me/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("POST", "/bff/me/exchange-accounts")]
    [InlineData("PATCH", "/bff/me/exchange-accounts/" + AccountId)]
    [InlineData("POST", "/bff/me/exchange-accounts/" + AccountId + "/verify")]
    [InlineData("PUT", "/bff/me/exchange-accounts/" + AccountId + "/credentials")]
    [InlineData("DELETE", "/bff/me/exchange-accounts/" + AccountId)]
    public async Task Unsafe_requests_without_antiforgery_token_are_rejected_before_the_api(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, AccountJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await SendAsync(browser, method, path, method is "DELETE" ? null : CreateBody, antiforgery: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Api.Count.Should().Be(0);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("POST", "/bff/me/exchange-accounts/" + AccountId + "/sync")]
    [InlineData("GET", "/bff/me/exchange-accounts/" + AccountId)]
    [InlineData("GET", "/bff/me/exchange-accounts/" + AccountId + "/portfolio")]
    [InlineData("PATCH", "/bff/me/exchange-accounts/not-a-guid")]
    [InlineData("DELETE", "/bff/me/exchange-accounts/..%2F..%2Fauth%2Fme")]
    [InlineData("GET", "/bff/me/positions")]
    [InlineData("GET", "/bff/api/v1/me/exchange-accounts")]
    [InlineData("POST", "/bff/me/exchange-accounts/" + AccountId + "/verify/extra")]
    public async Task Unknown_or_unplanned_bff_paths_return_not_found_without_reaching_the_api(string method, string path)
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, AccountJson));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var antiforgery = await browser.GetAntiforgeryTokenAsync();

        using var response = await SendAsync(browser, method, path, method is "GET" or "DELETE" ? null : "{}", antiforgery);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Api.Count.Should().Be(0);
    }

    [Fact]
    public async Task Query_string_is_not_forwarded_to_the_api()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, "{\"items\":[]}"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        using var response = await browser.Client.GetAsync("/bff/me/exchange-accounts?path=../positions&userId=other");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Api.Requests.Should().ContainSingle()
            .Which.RequestUri.AbsoluteUri.Should().Be(ApiCollection);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        BffBrowser browser,
        string method,
        string path,
        string? body,
        string? antiforgery)
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
