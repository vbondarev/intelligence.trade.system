using System.Net;
using Intelligence.TradeSystem.Api.Authentication;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PublicIssuerBackchannelHandlerTests
{
    [Fact]
    public async Task Rewrites_only_requests_for_the_configured_public_issuer()
    {
        var recordingHandler = new RecordingHandler();
        using var handler = new PublicIssuerBackchannelHandler(
            new Uri("http://public-identity.test"),
            new Uri("http://identity-internal.test"),
            recordingHandler);
        using var client = new HttpClient(handler);

        using var publicResponse = await client.GetAsync("http://public-identity.test/.well-known/jwks?version=1");
        using var arbitraryResponse = await client.GetAsync("https://arbitrary-host.example/keys");

        recordingHandler.RequestedUris.Should().Equal(
            new Uri("http://identity-internal.test/.well-known/jwks?version=1"),
            new Uri("https://arbitrary-host.example/keys"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
