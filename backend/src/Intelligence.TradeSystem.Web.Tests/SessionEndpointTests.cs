using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Web.Tests.Support;

namespace Intelligence.TradeSystem.Web.Tests;

public sealed class SessionEndpointTests
{
    [Fact]
    public async Task Anonymous_browser_receives_unauthenticated_session_without_user()
    {
        using var factory = new WebBffApplicationFactory();
        using var browser = factory.CreateBrowser();

        using var response = await browser.Client.GetAsync("/bff/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        document.RootElement.TryGetProperty("user", out _).Should().BeFalse();
        factory.Api.Count.Should().Be(0);
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Authenticated_session_returns_api_identity_without_token_fields()
    {
        using var factory = new WebBffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(UpstreamResponses.CurrentUser());
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessToken: "access-visible-only-to-bff");

        using var response = await browser.Client.GetAsync("/bff/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("authenticated", "user");
        root.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        root.GetProperty("user").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("userId", "subject");
        root.GetProperty("user").GetProperty("userId").GetGuid().Should().Be(UpstreamResponses.UserId);
        root.GetProperty("user").GetProperty("subject").GetString().Should().Be("user-subject");
        body.Should().NotContain("access-visible-only-to-bff")
            .And.NotContain("refresh-1")
            .And.NotContain("id-token-1")
            .And.NotContainEquivalentOf("token");

        var apiRequest = factory.Api.Requests.Should().ContainSingle().Subject;
        apiRequest.RequestUri.AbsoluteUri.Should().Be(WebBffApplicationFactory.ApiBaseAddress + "api/v1/auth/me");
        apiRequest.AuthorizationScheme.Should().Be("Bearer");
        apiRequest.AuthorizationParameter.Should().Be("access-visible-only-to-bff");
    }

    [Fact]
    public async Task Api_forbidden_is_returned_as_forbidden_without_ending_session()
    {
        using var factory = new WebBffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        await browser.GetSessionAsync(HttpStatusCode.Forbidden);

        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_api_identity_is_reported_as_temporarily_unavailable()
    {
        using var factory = new WebBffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(
            HttpStatusCode.OK,
            """{"userId":"00000000-0000-0000-0000-000000000000","subject":"user-subject","authenticated":true}"""));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();

        await browser.GetSessionAsync(HttpStatusCode.ServiceUnavailable);

        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Session_without_subject_is_ended()
    {
        using var factory = new WebBffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(subject: null);

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        browser.SessionCookie.Should().BeNull();
        factory.Api.Count.Should().Be(0);
    }
}
