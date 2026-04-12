using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class RootEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RootEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_Returns_Ok_And_Started_Payload()
    {
        using var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ServiceStatusDto>()
            ?? throw new InvalidOperationException("Root response payload was null.");

        payload.Service.Should().Be("Intelligence.TradeSystem.Api");
        payload.Status.Should().Be("Started");
    }

    private sealed class ServiceStatusDto
    {
        public string? Service { get; init; }

        public string? Status { get; init; }
    }
}

