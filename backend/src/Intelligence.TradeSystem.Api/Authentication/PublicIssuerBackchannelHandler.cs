namespace Intelligence.TradeSystem.Api.Authentication;

internal sealed class PublicIssuerBackchannelHandler(
    Uri publicIssuer,
    Uri backchannelBaseAddress,
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private readonly Uri publicIssuer = publicIssuer;
    private readonly Uri backchannelBaseAddress = backchannelBaseAddress;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } requestUri
            && IsPublicIssuerAuthority(requestUri))
        {
            request.RequestUri = RewriteAuthority(requestUri);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private bool IsPublicIssuerAuthority(Uri requestUri) =>
        string.Equals(requestUri.Scheme, publicIssuer.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(requestUri.Host, publicIssuer.Host, StringComparison.OrdinalIgnoreCase)
        && requestUri.Port == publicIssuer.Port;

    private Uri RewriteAuthority(Uri requestUri)
    {
        var builder = new UriBuilder(requestUri)
        {
            Scheme = backchannelBaseAddress.Scheme,
            Host = backchannelBaseAddress.Host,
            Port = backchannelBaseAddress.IsDefaultPort ? -1 : backchannelBaseAddress.Port
        };

        return builder.Uri;
    }
}
