using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class PublicIssuerBackchannelHandlerTests
{
    [Theory]
    [InlineData("http://identity:8080", "http://identity:8080/connect/token?x=1")]
    [InlineData("https://identity.internal", "https://identity.internal/connect/token?x=1")]
    public async Task Requests_to_public_issuer_are_sent_to_internal_address(string backchannel, string expected)
    {
        var inner = new RecordingHttpHandler();
        using var invoker = CreateInvoker(new Uri(backchannel), inner);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://localhost:8081/connect/token?x=1"),
            CancellationToken.None);

        inner.Requests.Single().RequestUri.AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData("http://localhost:8082/connect/token")]
    [InlineData("https://localhost:8081/connect/token")]
    [InlineData("http://other.example:8081/connect/token")]
    public async Task Other_authorities_are_not_rewritten(string requestUri)
    {
        var inner = new RecordingHttpHandler();
        using var invoker = CreateInvoker(new Uri("http://identity:8080"), inner);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, requestUri),
            CancellationToken.None);

        inner.Requests.Single().RequestUri.AbsoluteUri.Should().Be(requestUri);
    }

    private static HttpMessageInvoker CreateInvoker(Uri backchannel, HttpMessageHandler inner) =>
        new(new PublicIssuerBackchannelHandler(new Uri("http://localhost:8081/"), backchannel)
        {
            InnerHandler = inner,
        });
}
