using System.Globalization;
using System.Net;
using Intelligence.TradeSystem.Bff.Authentication;
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
            factory.Services.GetRequiredService<RefreshGateRegistry>().Count
                .Should().Be(0, "gate удаляется после последнего owner/waiter");
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
    public async Task Concurrent_requests_share_unavailable_refresh_without_repeating_grant()
    {
        const string subject = "user-subject";
        const int concurrentRequests = 5;
        using var factory = CreateFactoryWithApi();
        var tokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var grants = 0;
        factory.TokenEndpoint.Responder = async (_, _) =>
        {
            // Повторный grant тем же refresh token после ambiguous failure получил бы invalid_grant
            // и завершил session, поэтому любой второй вызов здесь делает тест красным.
            if (Interlocked.Increment(ref grants) > 1)
            {
                return UpstreamResponses.TokenError(HttpStatusCode.BadRequest, "invalid_grant");
            }

            tokenRequestReceived.TrySetResult();
            await releaseTokenResponse.Task;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };
        var registry = factory.Services.GetRequiredService<RefreshGateRegistry>();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(subject: subject, accessTokenLifetime: NearExpiry);

        var requests = Enumerable.Range(0, concurrentRequests)
            .Select(_ => browser.Client.GetAsync("/bff/auth/session"))
            .ToArray();
        HttpResponseMessage[] responses;
        try
        {
            await tokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => registry.UsersOf(subject) == concurrentRequests);
        }
        finally
        {
            releaseTokenResponse.TrySetResult();
            responses = await Task.WhenAll(requests);
        }

        try
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.ServiceUnavailable);
            factory.TokenEndpoint.Count.Should().Be(1);
            browser.SessionCookie.Should().NotBeNull();
            var stored = await browser.GetStoredTicketAsync();
            stored.Should().NotBeNull();
            stored!.Properties.GetTokenValue("access_token").Should().Be("access-1");
            stored.Properties.GetTokenValue("refresh_token").Should().Be("refresh-1");
            registry.Count.Should().Be(0);
            factory.Api.Count.Should().Be(0);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(
            UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2"));

        var session = await browser.GetSessionAsync();

        session.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        factory.TokenEndpoint.Count.Should().Be(2, "после cleanup gate новый request снова выполняет refresh");
        var refreshed = await browser.GetStoredTicketAsync();
        refreshed!.Properties.GetTokenValue("access_token").Should().Be("access-2");
        refreshed.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
        registry.Count.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_forced_refresh_after_api_unauthorized_shares_unavailable_result()
    {
        const string subject = "user-subject";
        const int concurrentRequests = 3;
        using var factory = new BffApplicationFactory();
        factory.Api.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var tokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var grants = 0;
        factory.TokenEndpoint.Responder = async (_, _) =>
        {
            if (Interlocked.Increment(ref grants) > 1)
            {
                return UpstreamResponses.TokenError(HttpStatusCode.BadRequest, "invalid_grant");
            }

            tokenRequestReceived.TrySetResult();
            await releaseTokenResponse.Task;
            throw new HttpRequestException("connection reset");
        };
        var registry = factory.Services.GetRequiredService<RefreshGateRegistry>();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(subject: subject, accessTokenLifetime: TimeSpan.FromMinutes(30));

        var requests = Enumerable.Range(0, concurrentRequests)
            .Select(_ => browser.Client.GetAsync("/bff/auth/session"))
            .ToArray();
        HttpResponseMessage[] responses;
        try
        {
            await tokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => registry.UsersOf(subject) == concurrentRequests);
        }
        finally
        {
            releaseTokenResponse.TrySetResult();
            responses = await Task.WhenAll(requests);
        }

        try
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.ServiceUnavailable);
            factory.TokenEndpoint.Count.Should().Be(1);
            factory.Api.Requests.Should().HaveCount(concurrentRequests)
                .And.OnlyContain(request => request.AuthorizationParameter == "access-1");
            var stored = await browser.GetStoredTicketAsync();
            stored.Should().NotBeNull();
            stored!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-1");
            registry.Count.Should().Be(0);
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
    public async Task Cancelled_refresh_waiter_releases_its_gate_reference_without_second_grant()
    {
        const string subject = "user-subject";
        using var factory = CreateFactoryWithApi();
        var tokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TokenEndpoint.Responder = async (_, _) =>
        {
            tokenRequestReceived.TrySetResult();
            await releaseTokenResponse.Task;
            return UpstreamResponses.TokenSuccess("access-2", refreshToken: "refresh-2");
        };
        var registry = factory.Services.GetRequiredService<RefreshGateRegistry>();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(subject: subject, accessTokenLifetime: NearExpiry);
        using var waiterCancellation = new CancellationTokenSource();

        var owner = browser.Client.GetAsync("/bff/auth/session");
        try
        {
            await tokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            registry.UsersOf(subject).Should().Be(1);

            var waiter = browser.Client.GetAsync("/bff/auth/session", waiterCancellation.Token);
            await WaitUntilAsync(() => registry.UsersOf(subject) == 2);
            await waiterCancellation.CancelAsync();
            await waiter.Awaiting(task => task).Should().ThrowAsync<OperationCanceledException>();

            await WaitUntilAsync(() => registry.UsersOf(subject) == 1);
            registry.Count.Should().Be(1, "gate владельца остаётся, пока refresh выполняется");
            owner.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseTokenResponse.TrySetResult();
        }

        using var response = await owner;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.TokenEndpoint.Count.Should().Be(1);
        registry.Count.Should().Be(0);
        var stored = await browser.GetStoredTicketAsync();
        stored!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
    }

    [Fact]
    public async Task Refresh_of_another_subject_does_not_wait_for_in_flight_refresh()
    {
        using var factory = CreateFactoryWithApi();
        var firstTokenRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TokenEndpoint.Responder = async (request, _) =>
        {
            if (request.Form["refresh_token"] == "refresh-a1")
            {
                firstTokenRequestReceived.TrySetResult();
                await releaseFirstTokenResponse.Task;
                return UpstreamResponses.TokenSuccess("access-a2", refreshToken: "refresh-a2");
            }

            return UpstreamResponses.TokenSuccess("access-b2", refreshToken: "refresh-b2");
        };
        var registry = factory.Services.GetRequiredService<RefreshGateRegistry>();
        using var first = factory.CreateBrowser();
        await first.SignInAsync(
            subject: "subject-a",
            accessToken: "access-a1",
            accessTokenLifetime: NearExpiry,
            refreshToken: "refresh-a1");
        using var second = factory.CreateBrowser();
        await second.SignInAsync(
            subject: "subject-b",
            accessToken: "access-b1",
            accessTokenLifetime: NearExpiry,
            refreshToken: "refresh-b1");

        var firstRequest = first.Client.GetAsync("/bff/auth/session");
        try
        {
            await firstTokenRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var secondSession = await second.GetSessionAsync().WaitAsync(TimeSpan.FromSeconds(10));

            secondSession.GetProperty("authenticated").GetBoolean().Should().BeTrue();
            firstRequest.IsCompleted.Should().BeFalse("refresh subject-a всё ещё заблокирован token endpoint");
            registry.UsersOf("subject-a").Should().Be(1);
            registry.UsersOf("subject-b").Should().Be(0);
            var secondStored = await second.GetStoredTicketAsync();
            secondStored!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-b2");
        }
        finally
        {
            releaseFirstTokenResponse.TrySetResult();
        }

        using var firstResponse = await firstRequest;
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.TokenEndpoint.Requests.Select(request => request.Form["refresh_token"])
            .Should().Equal("refresh-a1", "refresh-b1");
        factory.Api.Requests.Select(request => request.AuthorizationParameter)
            .Should().Equal("access-b2", "access-a2");
        var firstStored = await first.GetStoredTicketAsync();
        firstStored!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-a2");
        registry.Count.Should().Be(0);
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

    /// <summary>
    /// Ожидает состояния, которое request достигает на server side асинхронно относительно клиента.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Ожидаемое состояние refresh gate не достигнуто.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private static DateTimeOffset ParseExpiresAt(AuthenticationTicket ticket) =>
        DateTimeOffset.Parse(
            ticket.Properties.GetTokenValue("expires_at")!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
