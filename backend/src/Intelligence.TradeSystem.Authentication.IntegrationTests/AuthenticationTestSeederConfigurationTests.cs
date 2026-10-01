using FluentAssertions;
using Intelligence.TradeSystem.Authentication.TestSeeder.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

public sealed class AuthenticationTestSeederConfigurationTests
{
    public static TheoryData<string> RequiredKeys =>
    [
        "TestSeeder:Username",
        "TestSeeder:Password",
        "TestSeeder:ClientId",
        "TestSeeder:RedirectUri",
    ];

    [Fact]
    public void Settings_are_created_from_own_test_seeder_configuration_without_identity_connection_string()
    {
        var configuration = CreateConfiguration();

        var settings = TestSeederSettings.FromConfiguration(configuration);

        settings.Should().Be(new TestSeederSettings(
            "compose-smoke-user",
            "Compose-smoke-password-123",
            "compose-smoke-client",
            "http://client.test/callback"));
    }

    [Theory]
    [MemberData(nameof(RequiredKeys))]
    public void Missing_required_setting_fails_fast(string key)
    {
        var configuration = CreateConfiguration((key, null));

        var act = () => TestSeederSettings.FromConfiguration(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{key}*");
    }

    [Theory]
    [MemberData(nameof(RequiredKeys))]
    public void Empty_required_setting_fails_fast(string key)
    {
        var configuration = CreateConfiguration((key, string.Empty));

        var act = () => TestSeederSettings.FromConfiguration(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{key}*");
    }

    [Theory]
    [MemberData(nameof(RequiredKeys))]
    public void Whitespace_required_setting_fails_fast(string key)
    {
        var configuration = CreateConfiguration((key, "   "));

        var act = () => TestSeederSettings.FromConfiguration(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{key}*");
    }

    [Fact]
    public void Configuration_diagnostics_do_not_disclose_password()
    {
        const string password = "test-seeder-password-marker";
        var configuration = CreateConfiguration(
            ("TestSeeder:Password", password),
            ("TestSeeder:RedirectUri", "client.test/callback"));

        var act = () => TestSeederSettings.FromConfiguration(configuration);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().NotContain(password);
        exception.ToString().Should().NotContain(password);
    }

    [Theory]
    [InlineData("client.test/callback")]
    [InlineData("not a uri")]
    public void Non_absolute_redirect_uri_is_rejected_before_seeding(string redirectUri)
    {
        var configuration = CreateConfiguration(("TestSeeder:RedirectUri", redirectUri));

        var act = () => TestSeederSettings.FromConfiguration(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*TestSeeder:RedirectUri*");
    }

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TestSeeder:Username"] = "compose-smoke-user",
            ["TestSeeder:Password"] = "Compose-smoke-password-123",
            ["TestSeeder:ClientId"] = "compose-smoke-client",
            ["TestSeeder:RedirectUri"] = "http://client.test/callback",
        };

        foreach (var (key, value) in overrides)
        {
            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
