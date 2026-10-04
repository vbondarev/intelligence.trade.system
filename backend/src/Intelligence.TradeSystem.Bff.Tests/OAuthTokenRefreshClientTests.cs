using System.Net;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class OAuthTokenRefreshClientTests
{
    private const string TokenEndpoint = "http://identity.test/connect/token";

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
        result.ExpiresIn.Should().Be(TimeSpan.FromMinutes(10));
        result.ToString().Should().NotContain("access-2").And.NotContain("refresh-2");
        handler.Requests.Single().RequestUri.AbsoluteUri.Should().Be(TokenEndpoint);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"token_type":"Bearer","expires_in":60}""")]
    [InlineData("""{"access_token":"a","expires_in":0}""")]
    [InlineData("""{"access_token":"a","expires_in":"60"}""")]
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
    public async Task Caller_cancellation_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new RecordingHttpHandler
        {
            Responder = (_, token) => Task.FromCanceled<HttpResponseMessage>(token),
        };

        var act = () => CreateClient(handler).RefreshAsync("refresh-1", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
                "client",
                "secret"),
            NullLogger<OAuthTokenRefreshClient>.Instance);

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
