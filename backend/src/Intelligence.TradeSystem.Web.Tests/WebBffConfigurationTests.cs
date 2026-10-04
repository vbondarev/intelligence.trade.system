using Intelligence.TradeSystem.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Web.Tests;

public sealed class WebBffConfigurationTests
{
    private const string Secret = "configuration-secret-value-1f0e";

    [Fact]
    public void Valid_local_configuration_derives_metadata_and_backchannel_from_authority()
    {
        var configuration = WebBffConfiguration.Load(Build(), Environment("Development"));

        configuration.Oidc.Authority.Should().Be(new Uri("http://localhost:8081/"));
        configuration.Oidc.MetadataAddress.Should().Be(new Uri("http://localhost:8081/.well-known/openid-configuration"));
        configuration.Oidc.BackchannelBaseAddress.Should().Be(new Uri("http://localhost:8081"));
        configuration.Oidc.ClientId.Should().Be("trade-web-bff");
        configuration.Oidc.ClientSecret.Should().Be(Secret);
        configuration.ApiBaseAddress.Should().Be(new Uri("http://localhost:8080/"));
        configuration.Session.Lifetime.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void Explicit_internal_addresses_are_used()
    {
        var configuration = WebBffConfiguration.Load(
            Build(new Dictionary<string, string?>
            {
                ["Web:Oidc:MetadataAddress"] = "http://identity:8080/.well-known/openid-configuration",
                ["Web:Oidc:BackchannelBaseAddress"] = "http://identity:8080",
                ["Web:Session:Lifetime"] = "01:30:00",
            }),
            Environment("Testing"));

        configuration.Oidc.MetadataAddress.Should().Be(new Uri("http://identity:8080/.well-known/openid-configuration"));
        configuration.Oidc.BackchannelBaseAddress.Should().Be(new Uri("http://identity:8080"));
        configuration.Session.Lifetime.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void Production_accepts_https_endpoints()
    {
        var configuration = WebBffConfiguration.Load(
            Build(new Dictionary<string, string?>
            {
                ["Web:Oidc:Authority"] = "https://identity.example/",
                ["Web:Api:BaseAddress"] = "https://api.example/",
            }),
            Environment("Production"));

        configuration.Oidc.MetadataAddress.Should().Be(new Uri("https://identity.example/.well-known/openid-configuration"));
    }

    public static TheoryData<string, string?, string, string> InvalidConfigurations => new()
    {
        { "Web:Oidc:ClientId", null, "Development", "Web:Oidc:ClientId" },
        { "Web:Oidc:ClientId", " ", "Development", "Web:Oidc:ClientId" },
        { "Web:Oidc:ClientSecret", null, "Development", "Web:Oidc:ClientSecret" },
        { "Web:Oidc:Authority", null, "Development", "Web:Oidc:Authority" },
        { "Web:Oidc:Authority", "identity", "Development", "Web:Oidc:Authority" },
        { "Web:Oidc:Authority", "ftp://identity.example/", "Development", "Web:Oidc:Authority" },
        { "Web:Oidc:Authority", "http://user:pass@identity.example/", "Development", "Web:Oidc:Authority" },
        { "Web:Oidc:Authority", "http://identity.example/#fragment", "Development", "Web:Oidc:Authority" },
        { "Web:Oidc:MetadataAddress", "not a url", "Development", "Web:Oidc:MetadataAddress" },
        { "Web:Oidc:BackchannelBaseAddress", "relative/path", "Development", "Web:Oidc:BackchannelBaseAddress" },
        { "Web:Api:BaseAddress", null, "Development", "Web:Api:BaseAddress" },
        { "Web:Api:BaseAddress", "api", "Development", "Web:Api:BaseAddress" },
        { "Web:Oidc:Authority", "http://identity.example/", "Production", "Web:Oidc:Authority" },
        { "Web:Api:BaseAddress", "http://api.example/", "Staging", "Web:Api:BaseAddress" },
        { "Web:Session:Lifetime", "00:00:00", "Development", "Web:Session:Lifetime" },
        { "Web:Session:Lifetime", "1.00:00:01", "Development", "Web:Session:Lifetime" },
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
        if (environmentName != "Development" && key != "Web:Oidc:Authority")
        {
            overrides["Web:Oidc:Authority"] = "https://identity.example/";
        }

        if (environmentName != "Development" && key != "Web:Api:BaseAddress")
        {
            overrides["Web:Api:BaseAddress"] = "https://api.example/";
        }

        var act = () => WebBffConfiguration.Load(Build(overrides), Environment(environmentName));

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(expectedKey).And.NotContain(Secret);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Oidc_settings_do_not_expose_secret_through_to_string()
    {
        var configuration = WebBffConfiguration.Load(Build(), Environment("Development"));

        configuration.Oidc.ToString().Should().NotContain(Secret);
        configuration.ToString().Should().NotContain(Secret);
    }

    private static IConfiguration Build(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Web:Oidc:Authority"] = "http://localhost:8081/",
            ["Web:Oidc:ClientId"] = "trade-web-bff",
            ["Web:Oidc:ClientSecret"] = Secret,
            ["Web:Api:BaseAddress"] = "http://localhost:8080/",
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

        public string ApplicationName { get; set; } = "Intelligence.TradeSystem.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
