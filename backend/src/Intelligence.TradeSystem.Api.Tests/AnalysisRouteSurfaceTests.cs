using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class AnalysisRouteSurfaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AnalysisRouteSurfaceTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/analysis/snapshot")]
    [InlineData("/api/analysis/ai")]
    public async Task Get_Route_Returns_MethodNotAllowed(string route)
    {
        using var response = await _client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Unknown_Analysis_Route_Returns_NotFound()
    {
        using var response = await _client.GetAsync("/api/analysis/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}




