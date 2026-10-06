using System.Net;

namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Trust boundary для forwarded headers frontend reverse proxy.
/// </summary>
/// <remarks>
/// BFF работает за frontend service, который сохраняет public request context в <c>Host</c> и
/// <c>X-Forwarded-*</c>. Эти headers учитываются только от loopback и явно перечисленных сетей
/// reverse proxy: иначе произвольный client мог бы подменить scheme/host, из которых строятся
/// OIDC redirect URIs.
/// </remarks>
public sealed class BffForwardedHeadersOptions
{
    public const string SectionName = "Bff:ForwardedHeaders";

    /// <summary>
    /// CIDR-сети reverse proxy, которым разрешено передавать forwarded headers, дополнительно к loopback.
    /// </summary>
    public IReadOnlyList<string> KnownNetworks { get; init; } = [];

    internal IReadOnlyList<IPNetwork> Validate()
    {
        var networks = new List<IPNetwork>(KnownNetworks.Count);
        foreach (var value in KnownNetworks)
        {
            if (string.IsNullOrWhiteSpace(value) || !IPNetwork.TryParse(value.Trim(), out var network))
            {
                throw new InvalidOperationException(
                    "Bff:ForwardedHeaders:KnownNetworks должен содержать только CIDR-сети, например 172.16.0.0/12.");
            }

            networks.Add(network);
        }

        return networks;
    }
}
