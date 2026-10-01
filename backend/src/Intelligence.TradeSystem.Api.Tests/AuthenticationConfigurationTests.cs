using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class AuthenticationConfigurationTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly byte[] TestCredentialKey = new byte[32];

    private readonly ApiWebApplicationFactory factory;

    public AuthenticationConfigurationTests(ApiWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Production_rejects_an_insecure_backchannel_address()
    {
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Authentication:Issuer", "https://identity.example");
            builder.UseSetting(
                "Authentication:MetadataAddress",
                "https://identity.example/.well-known/openid-configuration");
            builder.UseSetting("Authentication:BackchannelBaseAddress", "http://identity-internal:8080");
        });

        var act = () => configuredFactory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Authentication:BackchannelBaseAddress*");
    }

    [Fact]
    public void Missing_audience_fails_during_host_creation()
    {
        using var isolatedFactory = CreateIsolatedFactory(audience: null);

        var act = () => isolatedFactory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Authentication:Audience*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_audience_fails_during_host_creation(string audience)
    {
        using var isolatedFactory = CreateIsolatedFactory(audience);

        var act = () => isolatedFactory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Authentication:Audience*");
    }

    [Fact]
    public void Configured_audience_is_used_for_jwt_audience_validation()
    {
        using var isolatedFactory = CreateIsolatedFactory("custom-trade-api");

        var options = isolatedFactory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Audience.Should().Be("custom-trade-api");
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidAudience.Should().Be("custom-trade-api");
    }

    // Изолированная factory не наследует Audience из общей ApiWebApplicationFactory,
    // поэтому отсутствие настройки проверяется без скрытых источников значения.
    private static WebApplicationFactory<Program> CreateIsolatedFactory(string? audience)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:TradeSystem",
                    "Host=127.0.0.1;Port=1;Database=tradesystem;Timeout=1;Command Timeout=1");
                builder.UseSetting("CredentialProtection:ActiveKeyId", "test");
                builder.UseSetting(
                    "CredentialProtection:Keys:test",
                    Convert.ToBase64String(TestCredentialKey));
                if (audience is not null)
                {
                    builder.UseSetting("Authentication:Audience", audience);
                }
            });
    }
}
