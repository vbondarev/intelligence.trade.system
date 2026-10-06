using System.Net;
using Intelligence.TradeSystem.Bff.Api;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class CurrentUserApiClientTests
{
    [Fact]
    public async Task Valid_identity_is_returned_and_bearer_token_is_sent()
    {
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(UpstreamResponses.CurrentUser("subject-1")),
        };

        var result = await CreateClient(handler).GetCurrentUserAsync("access-token", CancellationToken.None);

        result.Status.Should().Be(CurrentUserApiStatus.Success);
        result.User!.UserId.Should().Be(UpstreamResponses.UserId);
        result.User.Subject.Should().Be("subject-1");
        var request = handler.Requests.Single();
        request.RequestUri.AbsoluteUri.Should().Be("http://api.test/api/v1/auth/me");
        request.AuthorizationScheme.Should().Be("Bearer");
        request.AuthorizationParameter.Should().Be("access-token");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, nameof(CurrentUserApiStatus.Unauthorized))]
    [InlineData(HttpStatusCode.Forbidden, nameof(CurrentUserApiStatus.Forbidden))]
    [InlineData(HttpStatusCode.InternalServerError, nameof(CurrentUserApiStatus.Unavailable))]
    [InlineData(HttpStatusCode.NotFound, nameof(CurrentUserApiStatus.Unavailable))]
    public async Task Status_codes_are_mapped(HttpStatusCode statusCode, string expectedStatus)
    {
        var expected = Enum.Parse<CurrentUserApiStatus>(expectedStatus);
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)),
        };

        var result = await CreateClient(handler).GetCurrentUserAsync("access-token", CancellationToken.None);

        result.Status.Should().Be(expected);
        result.User.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"userId":"4f0f4b1e-6f55-4c4b-9f56-0c0d5a1b2c3d","subject":"s","authenticated":false}""")]
    [InlineData("""{"userId":"4f0f4b1e-6f55-4c4b-9f56-0c0d5a1b2c3d","subject":" ","authenticated":true}""")]
    [InlineData("null")]
    public async Task Malformed_identity_is_unavailable(string json)
    {
        var handler = new RecordingHttpHandler
        {
            Responder = (_, _) => Task.FromResult(RecordingHttpHandler.Json(HttpStatusCode.OK, json)),
        };

        var result = await CreateClient(handler).GetCurrentUserAsync("access-token", CancellationToken.None);

        result.Status.Should().Be(CurrentUserApiStatus.Unavailable);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("connection refused"),
        new TaskCanceledException("timeout"),
        new BrokenCircuitException("circuit open"),
    };

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Transport_failures_are_unavailable(Exception failure)
    {
        var handler = new RecordingHttpHandler { Responder = (_, _) => throw failure };

        var result = await CreateClient(handler).GetCurrentUserAsync("access-token", CancellationToken.None);

        result.Status.Should().Be(CurrentUserApiStatus.Unavailable);
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

        var act = () => CreateClient(handler).GetCurrentUserAsync("access-token", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static CurrentUserApiClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://api.test/") },
            NullLogger<CurrentUserApiClient>.Instance);
}
