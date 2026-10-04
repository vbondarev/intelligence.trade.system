using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Configuration;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class BffConfigurationTests
{
    private const string Secret = "configuration-secret-value-1f0e";

    [Fact]
    public void Valid_local_configuration_derives_metadata_and_backchannel_from_authority()
    {
        var configuration = BffConfiguration.Load(Build(), Environment("Development"));

        configuration.Oidc.Authority.Should().Be(new Uri("http://localhost:8081/"));
        configuration.Oidc.MetadataAddress.Should().Be(new Uri("http://localhost:8081/.well-known/openid-configuration"));
        configuration.Oidc.BackchannelBaseAddress.Should().Be(new Uri("http://localhost:8081"));
        configuration.Oidc.ClientId.Should().Be("trade-web-bff");
        configuration.Oidc.ClientSecret.Should().Be(Secret);
        configuration.ApiBaseAddress.Should().Be(new Uri("http://localhost:8080/"));
        configuration.Session.Lifetime.Should().Be(TimeSpan.FromHours(8));
        configuration.Token.RefreshSkew.Should().Be(TimeSpan.FromMinutes(1));
        configuration.Token.EndpointTimeout.Should().Be(TimeSpan.FromSeconds(10));
        configuration.TrustedProxyNetworks.Should().BeEmpty();
    }

    [Fact]
    public void Zero_refresh_skew_is_distinguished_from_missing_value()
    {
        var configuration = BffConfiguration.Load(
            Build(new Dictionary<string, string?>
            {
                ["Bff:Token:RefreshSkew"] = "00:00:00",
                ["Bff:Token:EndpointTimeout"] = "00:00:03",
            }),
            Environment("Development"));

        configuration.Token.RefreshSkew.Should().Be(TimeSpan.Zero);
        configuration.Token.EndpointTimeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Trusted_proxy_networks_are_parsed_from_cidr()
    {
        var configuration = BffConfiguration.Load(
            Build(new Dictionary<string, string?>
            {
                ["Bff:ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
                ["Bff:ForwardedHeaders:KnownNetworks:1"] = " fd00::/8 ",
            }),
            Environment("Development"));

        configuration.TrustedProxyNetworks.Should().Equal(
            System.Net.IPNetwork.Parse("172.16.0.0/12"),
            System.Net.IPNetwork.Parse("fd00::/8"));
    }

    [Fact]
    public void Explicit_internal_addresses_are_used()
    {
        var configuration = BffConfiguration.Load(
            Build(new Dictionary<string, string?>
            {
                ["Bff:Oidc:MetadataAddress"] = "http://identity:8080/.well-known/openid-configuration",
                ["Bff:Oidc:BackchannelBaseAddress"] = "http://identity:8080",
                ["Bff:Session:Lifetime"] = "01:30:00",
            }),
            Environment("Testing"));

        configuration.Oidc.MetadataAddress.Should().Be(new Uri("http://identity:8080/.well-known/openid-configuration"));
        configuration.Oidc.BackchannelBaseAddress.Should().Be(new Uri("http://identity:8080"));
        configuration.Session.Lifetime.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void Production_accepts_https_endpoints()
    {
        var configuration = BffConfiguration.Load(
            Build(new Dictionary<string, string?>
            {
                ["Bff:Oidc:Authority"] = "https://identity.example/",
                ["Bff:Api:BaseAddress"] = "https://api.example/",
            }),
            Environment("Production"));

        configuration.Oidc.MetadataAddress.Should().Be(new Uri("https://identity.example/.well-known/openid-configuration"));
    }

    public static TheoryData<string, string?, string, string> InvalidConfigurations => new()
    {
        { "Bff:Oidc:ClientId", null, "Development", "Bff:Oidc:ClientId" },
        { "Bff:Oidc:ClientId", " ", "Development", "Bff:Oidc:ClientId" },
        { "Bff:Oidc:ClientSecret", null, "Development", "Bff:Oidc:ClientSecret" },
        { "Bff:Oidc:Authority", null, "Development", "Bff:Oidc:Authority" },
        { "Bff:Oidc:Authority", "identity", "Development", "Bff:Oidc:Authority" },
        { "Bff:Oidc:Authority", "ftp://identity.example/", "Development", "Bff:Oidc:Authority" },
        { "Bff:Oidc:Authority", "http://user:pass@identity.example/", "Development", "Bff:Oidc:Authority" },
        { "Bff:Oidc:Authority", "http://identity.example/#fragment", "Development", "Bff:Oidc:Authority" },
        { "Bff:Oidc:MetadataAddress", "not a url", "Development", "Bff:Oidc:MetadataAddress" },
        { "Bff:Oidc:BackchannelBaseAddress", "relative/path", "Development", "Bff:Oidc:BackchannelBaseAddress" },
        { "Bff:Api:BaseAddress", null, "Development", "Bff:Api:BaseAddress" },
        { "Bff:Api:BaseAddress", "api", "Development", "Bff:Api:BaseAddress" },
        { "Bff:Oidc:Authority", "http://identity.example/", "Production", "Bff:Oidc:Authority" },
        { "Bff:Api:BaseAddress", "http://api.example/", "Staging", "Bff:Api:BaseAddress" },
        { "Bff:Session:Lifetime", "00:00:00", "Development", "Bff:Session:Lifetime" },
        { "Bff:Session:Lifetime", "1.00:00:01", "Development", "Bff:Session:Lifetime" },
        { "Bff:Token:RefreshSkew", null, "Development", "Bff:Token:RefreshSkew" },
        { "Bff:Token:RefreshSkew", "-00:00:01", "Development", "Bff:Token:RefreshSkew" },
        { "Bff:Token:EndpointTimeout", null, "Development", "Bff:Token:EndpointTimeout" },
        { "Bff:Token:EndpointTimeout", "00:00:00", "Development", "Bff:Token:EndpointTimeout" },
        { "Bff:Token:EndpointTimeout", "-00:00:01", "Development", "Bff:Token:EndpointTimeout" },
        { "Bff:ForwardedHeaders:KnownNetworks:0", "proxy", "Development", "Bff:ForwardedHeaders:KnownNetworks" },
        { "Bff:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33", "Development", "Bff:ForwardedHeaders:KnownNetworks" },
    };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void Invalid_configuration_fails_fast_without_secret_disclosure(
        string key,
        string? value,
        string environmentName,
        string expectedKey)
    {
        var overrides = new Dictionary<string, string?> { [key] = value };
        if (environmentName != "Development" && key != "Bff:Oidc:Authority")
        {
            overrides["Bff:Oidc:Authority"] = "https://identity.example/";
        }

        if (environmentName != "Development" && key != "Bff:Api:BaseAddress")
        {
            overrides["Bff:Api:BaseAddress"] = "https://api.example/";
        }

        var act = () => BffConfiguration.Load(Build(overrides), Environment(environmentName));

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(expectedKey).And.NotContain(Secret);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Host_takes_default_token_settings_from_appsettings()
    {
        using var factory = new BffApplicationFactory();

        var tokenSettings = factory.Services.GetRequiredService<BffTokenSettings>();
        var tokenClient = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(OAuthTokenRefreshClient.HttpClientName);

        tokenSettings.RefreshSkew.Should().Be(TimeSpan.FromMinutes(1));
        tokenSettings.EndpointTimeout.Should().Be(TimeSpan.FromSeconds(10));
        tokenClient.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Configured_endpoint_timeout_is_applied_to_token_endpoint_client()
    {
        using var factory = new BffApplicationFactory(new Dictionary<string, string?>
        {
            ["Bff:Token:EndpointTimeout"] = "00:00:07",
        });

        var tokenClient = factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(OAuthTokenRefreshClient.HttpClientName);

        tokenClient.Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Oidc_settings_do_not_expose_secret_through_to_string()
    {
        var configuration = BffConfiguration.Load(Build(), Environment("Development"));

        configuration.Oidc.ToString().Should().NotContain(Secret);
        configuration.ToString().Should().NotContain(Secret);
    }

    private static IConfiguration Build(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bff:Oidc:Authority"] = "http://localhost:8081/",
            ["Bff:Oidc:ClientId"] = "trade-web-bff",
            ["Bff:Oidc:ClientSecret"] = Secret,
            ["Bff:Api:BaseAddress"] = "http://localhost:8080/",
            ["Bff:Token:RefreshSkew"] = "00:01:00",
            ["Bff:Token:EndpointTimeout"] = "00:00:10",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static TestHostEnvironment Environment(string name) => new(name);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Intelligence.TradeSystem.Bff.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
