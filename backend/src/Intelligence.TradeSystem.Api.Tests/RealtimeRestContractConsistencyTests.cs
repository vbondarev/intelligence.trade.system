using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class RealtimeRestContractConsistencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RealtimeRestContractConsistencyTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Realtime_events_have_an_explicit_rest_recovery_resource()
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["exchangeAccount.updated"] = "GET /api/v1/exchange-accounts",
            ["portfolio.updated"] = "GET /api/v1/exchange-accounts/{id}/portfolio",
            ["position.updated"] = "GET /api/v1/positions/{id}",
            ["evaluation.updated"] = "GET /api/v1/positions/{id}/evaluation",
        };

        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/hubs/v1/updates", out _).Should().BeFalse();

        mappings.Values.Select(mapping =>
        {
            var parts = mapping.Split(' ', 2);
            return paths.GetProperty(parts[1]).GetProperty(parts[0].ToLowerInvariant());
        }).Should().HaveCount(mappings.Count);
    }
}
