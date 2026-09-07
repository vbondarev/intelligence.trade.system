using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class AuthenticationConfigurationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public AuthenticationConfigurationTests(WebApplicationFactory<Program> factory)
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
}
