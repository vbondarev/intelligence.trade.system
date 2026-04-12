using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Returns_Ok_And_Healthy_Payload()
    {
        using var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>()
            ?? throw new InvalidOperationException("Health response payload was null.");

        payload.Service.Should().Be("Intelligence.TradeSystem.Api");
        payload.Status.Should().Be("Healthy");
    }
}

