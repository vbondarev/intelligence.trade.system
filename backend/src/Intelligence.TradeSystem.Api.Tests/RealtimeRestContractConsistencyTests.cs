using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Realtime.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class RealtimeRestContractConsistencyTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RealtimeRestContractConsistencyTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Realtime_events_have_an_explicit_rest_recovery_resource()
    {
        var recoveryResources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RealtimeEventNames.ExchangeAccountUpdated] = "GET /api/v1/exchange-accounts",
            [RealtimeEventNames.PortfolioUpdated] = "GET /api/v1/exchange-accounts/{id}/portfolio",
            [RealtimeEventNames.PositionUpdated] = "GET /api/v1/positions/{id}",
            [RealtimeEventNames.EvaluationUpdated] = "GET /api/v1/positions/{id}/evaluation",
        };

        recoveryResources.Keys
            .Should()
            .Equal(
                "exchangeAccount.updated",
                "portfolio.updated",
                "position.updated",
                "evaluation.updated");

        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/hubs/v1/updates", out _).Should().BeFalse();

        recoveryResources.Select(mapping =>
        {
            var parts = mapping.Value.Split(' ', 2);
            return paths.GetProperty(parts[1]).GetProperty(parts[0].ToLowerInvariant());
        }).Should().HaveCount(recoveryResources.Count);
    }
}
