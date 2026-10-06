using System.Net;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class OAuthTokenRefreshClientTests
{
    private const string TokenEndpoint = "http://identity.test/connect/token";

    /// <summary>
    /// Помещается в <see cref="TimeSpan"/> (около 15 800 лет), но <see cref="Now"/> + это значение
    /// выходит за <see cref="DateTimeOffset.MaxValue"/>.
    /// </summary>
    private const long ExpiresInBeyondDateTimeOffsetRange = 500_000_000_000;

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_response_returns_all_issued_tokens()
    {
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(
                UpstreamResponses.TokenSuccess("access-2", "refresh-2", 600, "id-2")),
        };

        var result = await CreateClient(handler).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Succeeded);
        result.AccessToken.Should().Be("access-2");
        result.RefreshToken.Should().Be("refresh-2");
        result.IdToken.Should().Be("id-2");
        result.ExpiresAt.Should().Be(Now.AddMinutes(10));
        result.ToString().Should().NotContain("access-2").And.NotContain("refresh-2");
        handler.Requests.Single().RequestUri.AbsoluteUri.Should().Be(TokenEndpoint);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"token_type":"Bearer","expires_in":60}""")]
    [InlineData("""{"access_token":"a","expires_in":0}""")]
    [InlineData("""{"access_token":"a","expires_in":"60"}""")]
    [InlineData("""{"access_token":"a","expires_in":922337203686}""")]
    [InlineData("""{"access_token":"a","expires_in":9223372036854775807}""")]
    [InlineData("""{"access_token":"a"}""")]
    [InlineData("[]")]
    public async Task Malformed_success_response_is_unavailable(string json)
    {
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, json)),
        };

        var result = await CreateClient(handler).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
    }

    [Fact]
    public async Task Expires_in_within_time_span_but_beyond_date_time_offset_range_is_unavailable()
    {
        var fitsTimeSpan = () => TimeSpan.FromSeconds(ExpiresInBeyondDateTimeOffsetRange);
        fitsTimeSpan.Should().NotThrow();
        TimeSpan.FromSeconds(ExpiresInBeyondDateTimeOffsetRange).Should().BeGreaterThan(DateTimeOffset.MaxValue - Now);
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(
                UpstreamResponses.TokenSuccess("access-2", "refresh-2", ExpiresInBeyondDateTimeOffsetRange)),
        };

        var result = await CreateClient(handler).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Expires_in_is_bounded_by_date_time_offset_range_relative_to_current_time()
    {
        var maxExpiresInSeconds = (DateTimeOffset.MaxValue - Now).Ticks / TimeSpan.TicksPerSecond;
        var expiresIn = maxExpiresInSeconds;
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(UpstreamResponses.TokenSuccess("access-2", expiresIn: expiresIn)),
        };
        var client = CreateClient(handler);

        var atBoundary = await client.RefreshAsync("refresh-1", CancellationToken.None);
        expiresIn = maxExpiresInSeconds + 1;
        var beyondBoundary = await client.RefreshAsync("refresh-1", CancellationToken.None);

        atBoundary.Status.Should().Be(OAuthTokenRefreshStatus.Succeeded);
        atBoundary.ExpiresAt.Should().Be(Now.AddTicks(maxExpiresInSeconds * TimeSpan.TicksPerSecond));
        beyondBoundary.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""", nameof(OAuthTokenRefreshStatus.Rejected))]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""", nameof(OAuthTokenRefreshStatus.Rejected))]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_request"}""", nameof(OAuthTokenRefreshStatus.Unavailable))]
    [InlineData(HttpStatusCode.BadRequest, "not json", nameof(OAuthTokenRefreshStatus.Unavailable))]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":"invalid_grant"}""", nameof(OAuthTokenRefreshStatus.Unavailable))]
    public async Task Error_responses_are_classified(HttpStatusCode statusCode, string json, string expectedStatus)
    {
        var expected = Enum.Parse<OAuthTokenRefreshStatus>(expectedStatus);
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(statusCode, json)),
        };

        var result = await CreateClient(handler).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(expected);
    }

    [Fact]
    public async Task Timeout_is_unavailable()
    {
        var handler = new RecordingHttpHandler { Responder = (_, _) => throw new TaskCanceledException("timeout") };

        var result = await CreateClient(handler).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
    }

    [Fact]
    public async Task Caller_cancellation_before_grant_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new RecordingHttpHandler
        {
            Responder = (_, token) => Task.FromCanceled<HttpResponseMessage>(token),
        };

        var act = () => CreateClient(handler).RefreshAsync("refresh-1", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task Caller_cancellation_after_grant_starts_does_not_abort_token_request()
    {
        using var caller = new CancellationTokenSource();
        var handler = new RecordingHttpHandler
        {
            Responder = (_, token) =>
            {
                caller.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(UpstreamResponses.TokenSuccess("access-2", "refresh-2", 600, "id-2"));
            },
        };

        var result = await CreateClient(handler).RefreshAsync("refresh-1", caller.Token);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Succeeded);
        result.RefreshToken.Should().Be("refresh-2");
        caller.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_configuration_manager_is_unavailable()
    {
        var handler = new RecordingHttpHandler();

        var result = await CreateClient(handler, configurationManager: null).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task Discovery_without_token_endpoint_is_unavailable()
    {
        var handler = new RecordingHttpHandler();
        var manager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration());

        var result = await CreateClient(handler, manager).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task Discovery_failure_is_unavailable()
    {
        var handler = new RecordingHttpHandler();

        var result = await CreateClient(handler, new FailingConfigurationManager()).RefreshAsync("refresh-1", CancellationToken.None);

        result.Status.Should().Be(OAuthTokenRefreshStatus.Unavailable);
        handler.Count.Should().Be(0);
    }

    private static OAuthTokenRefreshClient CreateClient(HttpMessageHandler handler) =>
        CreateClient(
            handler,
            new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { TokenEndpoint = TokenEndpoint }));

    private static OAuthTokenRefreshClient CreateClient(
        HttpMessageHandler handler,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager) =>
        new(
            new StaticHttpClientFactory(handler),
            new StaticOptionsMonitor(new OpenIdConnectOptions { ConfigurationManager = configurationManager }),
            new BffOidcSettings(
                new Uri("http://identity.test/"),
                new Uri("http://identity.test/.well-known/openid-configuration"),
                new Uri("http://identity.test/"),
                BffOidcOptions.CanonicalClientId,
                "secret"),
            new NeverStoppingLifetime(),
            new MutableTimeProvider(Now),
            NullLogger<OAuthTokenRefreshClient>.Instance);

    private sealed class NeverStoppingLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StaticOptionsMonitor(OpenIdConnectOptions options) : IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue => options;

        public OpenIdConnectOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
    }

    private sealed class FailingConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("discovery unavailable");

        public void RequestRefresh()
        {
        }
    }
}
