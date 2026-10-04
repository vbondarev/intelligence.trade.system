using System.Globalization;
using System.Net;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class TokenRefreshTests
{
    private static readonly TimeSpan NearExpiry = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Valid_access_token_is_not_refreshed()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromMinutes(10));

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        factory.TokenEndpoint.Count.Should().Be(0);
        factory.Api.Requests.Single().AuthorizationParameter.Should().Be("access-1");
    }

    [Fact]
    public async Task Access_token_within_refresh_skew_is_refreshed_and_rotated_tokens_are_stored()
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2", expiresIn: 900, idToken: "id-token-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        var tokenRequest = factory.TokenEndpoint.Requests.Should().ContainSingle().Subject;
        tokenRequest.Method.Should().Be(HttpMethod.Post);
        tokenRequest.RequestUri.AbsoluteUri.Should().Be(BffApplicationFactory.OidcConfiguration.TokenEndpoint);
        tokenRequest.Form.Should().Contain(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "refresh-1",
            ["client_id"] = BffApplicationFactory.ClientId,
            ["client_secret"] = BffApplicationFactory.ClientSecret,
        });
        factory.Api.Requests.Single().AuthorizationParameter.Should().Be("access-2");

        var stored = await browser.GetStoredTicketAsync();
        stored!.Properties.GetTokenValue("access_token").Should().Be("access-2");
        stored.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
        stored.Properties.GetTokenValue("id_token").Should().Be("id-token-2");
        ParseExpiresAt(stored).Should().Be(factory.Time.GetUtcNow().AddSeconds(900));

        await browser.GetSessionAsync();
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Requests[^1].AuthorizationParameter.Should().Be("access-2");
    }

    [Theory]
    [InlineData(61, false)]
    [InlineData(60, true)]
    [InlineData(59, true)]
    public async Task Fixed_refresh_skew_of_60_seconds_decides_whether_access_token_is_refreshed(
        int accessTokenLifetimeSeconds,
        bool refreshExpected)
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(accessTokenLifetimeSeconds));

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        factory.TokenEndpoint.Count.Should().Be(refreshExpected ? 1 : 0);
        factory.Api.Requests.Single().AuthorizationParameter.Should().Be(refreshExpected ? "access-2" : "access-1");
    }

    [Fact]
    public async Task Refresh_without_new_refresh_token_keeps_previous_refresh_token()
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(UpstreamResponses.TokenSuccess("access-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        await browser.GetSessionAsync();

        var stored = await browser.GetStoredTicketAsync();
        stored!.Properties.GetTokenValue("access_token").Should().Be("access-2");
        stored.Properties.GetTokenValue("refresh_token").Should().Be("refresh-1");
        stored.Properties.GetTokenValue("id_token").Should().Be("id-token-1");
    }

    [Fact]
    public async Task Invalid_grant_ends_bff_session()
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenError(HttpStatusCode.BadRequest, "invalid_grant"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);
        var sessionKey = browser.GetSessionKey();

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        browser.SessionCookie.Should().BeNull();
        (await factory.Services.GetTicketStore().RetrieveAsync(sessionKey!)).Should().BeNull();
        factory.Api.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Identity_server_error_keeps_session_and_is_not_retried(HttpStatusCode statusCode)
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(statusCode));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        await browser.GetSessionAsync(HttpStatusCode.ServiceUnavailable);

        factory.TokenEndpoint.Count.Should().Be(1, "refresh token grant нельзя повторять автоматически");
        var stored = await browser.GetStoredTicketAsync();
        stored!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-1");
        factory.Api.Count.Should().Be(0);
    }

    [Fact]
    public async Task Expires_in_beyond_date_time_offset_range_keeps_session_and_returns_service_unavailable()
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2", expiresIn: 500_000_000_000));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        await browser.GetSessionAsync(HttpStatusCode.ServiceUnavailable);

        factory.TokenEndpoint.Count.Should().Be(1);
        browser.SessionCookie.Should().NotBeNull();
        var stored = await browser.GetStoredTicketAsync();
        stored!.Properties.GetTokenValue("access_token").Should().Be("access-1");
        factory.Api.Count.Should().Be(0);
    }

    [Fact]
    public async Task Identity_network_error_keeps_session_and_is_not_retried()
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => throw new HttpRequestException("connection refused");
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        await browser.GetSessionAsync(HttpStatusCode.ServiceUnavailable);

        factory.TokenEndpoint.Count.Should().Be(1);
        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Non_grant_token_error_keeps_session()
    {
        using var factory = CreateFactoryWithApi();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenError(HttpStatusCode.Unauthorized, "invalid_client"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        await browser.GetSessionAsync(HttpStatusCode.ServiceUnavailable);

        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Session_without_refresh_token_is_ended_when_access_token_expires()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry, refreshToken: null);

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        factory.TokenEndpoint.Count.Should().Be(0);
        browser.SessionCookie.Should().BeNull();
    }

    [Fact]
    public async Task Single_api_unauthorized_triggers_one_forced_refresh_and_one_retry()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (request, _) => Task.FromResult(
            request.AuthorizationParameter == "access-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : UpstreamResponses.CurrentUser());
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromMinutes(30));

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Requests.Select(request => request.AuthorizationParameter)
            .Should().Equal("access-1", "access-2");
    }

    [Fact]
    public async Task Repeated_api_unauthorized_ends_session_without_loop()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromMinutes(30));

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Count.Should().Be(2);
        browser.SessionCookie.Should().BeNull();

        var next = await browser.GetSessionAsync();
        next.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        factory.TokenEndpoint.Count.Should().Be(1);
        factory.Api.Count.Should().Be(2);
    }

    [Fact]
    public async Task Api_unauthorized_followed_by_rejected_refresh_ends_session()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenError(HttpStatusCode.BadRequest, "invalid_grant"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromMinutes(30));

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        factory.Api.Count.Should().Be(1);
    }

    [Fact]
    public async Task Api_unauthorized_followed_by_unavailable_identity_returns_service_unavailable()
    {
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromMinutes(30));

        await browser.GetSessionAsync(HttpStatusCode.ServiceUnavailable);

        (await browser.GetStoredTicketAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_requests_of_one_session_use_single_refresh_grant()
    {
        using var factory = CreateFactoryWithApi();
        var tokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TokenEndpoint.Responder = async (_, cancellationToken) =>
        {
            tokenRequestReceived.TrySetResult();
            await releaseTokenResponse.Task.WaitAsync(cancellationToken);
            return UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2");
        };
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        var requests = Enumerable.Range(0, 5)
            .Select(_ => browser.Client.GetAsync("/bff/auth/session"))
            .ToArray();
        await tokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        releaseTokenResponse.SetResult();
        var responses = await Task.WhenAll(requests);

        try
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
            factory.TokenEndpoint.Count.Should().Be(1);
            factory.Api.Requests.Should().OnlyContain(request => request.AuthorizationParameter == "access-2");
            var stored = await browser.GetStoredTicketAsync();
            stored!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Active_refresh_lock_is_not_stored_in_the_expiring_session_cache()
    {
        using var factory = CreateFactoryWithApi();
        var tokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TokenEndpoint.Responder = async (_, _) =>
        {
            tokenRequestReceived.TrySetResult();
            await releaseTokenResponse.Task;
            return UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2");
        };
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);

        var inFlight = browser.Client.GetAsync("/bff/auth/session");
        await tokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        factory.Services.GetRequiredService<IMemoryCache>()
            .TryGetValue("bff-refresh-lock:user-subject", out _)
            .Should().BeFalse();

        releaseTokenResponse.SetResult();
        using var response = await inFlight;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.TokenEndpoint.Count.Should().Be(1);
    }

    [Fact]
    public async Task Browser_disconnect_during_refresh_still_stores_rotated_tokens()
    {
        using var factory = CreateFactoryWithApi();
        var tokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TokenEndpoint.Responder = async (_, cancellationToken) =>
        {
            tokenRequestReceived.TrySetResult();
            await releaseTokenResponse.Task.WaitAsync(cancellationToken);
            return UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2");
        };
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);
        using var disconnect = new CancellationTokenSource();

        var inFlight = browser.Client.GetAsync("/bff/auth/session", disconnect.Token);
        await tokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await disconnect.CancelAsync();
        releaseTokenResponse.SetResult();
        await inFlight.Awaiting(task => task).Should().ThrowAsync<OperationCanceledException>();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        AuthenticationTicket? stored = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            stored = await browser.GetStoredTicketAsync();
            if (stored?.Properties.GetTokenValue("refresh_token") == "refresh-2")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        stored.Should().NotBeNull();
        stored!.Properties.GetTokenValue("access_token").Should().Be("access-2");
        stored.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
    }

    [Fact]
    public async Task Session_ended_by_parallel_request_is_not_refreshed_again()
    {
        using var factory = CreateFactoryWithApi();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: NearExpiry);
        await factory.Services.GetTicketStore().RemoveAsync(browser.GetSessionKey()!);

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        factory.TokenEndpoint.Count.Should().Be(0);
    }

    private static BffApplicationFactory CreateFactoryWithApi(IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = new BffApplicationFactory(settings);
        factory.Api.Responder = (_, _) => Task.FromResult(UpstreamResponses.CurrentUser());
        return factory;
    }

    private static DateTimeOffset ParseExpiresAt(AuthenticationTicket ticket) =>
        DateTimeOffset.Parse(
            ticket.Properties.GetTokenValue("expires_at")!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
