using Intelligence.TradeSystem.Bff.Configuration;
using Microsoft.Extensions.Configuration;
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
