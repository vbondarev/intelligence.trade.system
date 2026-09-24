using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public HealthEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Alive_Endpoint_Remains_Healthy_When_PostgreSql_Is_Unavailable()
    {
        using var client = _factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Authentication:Issuer", "https://identity.test");
                builder.UseSetting("Authentication:MetadataAddress", "https://identity.test/.well-known/openid-configuration");
            })
            .CreateClient();

        using var response = await client.GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_Endpoint_Reports_Unavailable_PostgreSql_As_Not_Ready()
    {
        using var client = _factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Authentication:Issuer", "https://identity.test");
                builder.UseSetting(
                    "Authentication:MetadataAddress",
                    "https://identity.test/.well-known/openid-configuration");
            })
            .CreateClient();

        using var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
