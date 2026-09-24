using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionMarketSwaggerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public PositionMarketSwaggerTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Swagger_describes_position_market_and_candles_contracts()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var paths = root.GetProperty("paths");
        var market = paths.GetProperty("/api/v1/positions/{id}/market").GetProperty("get");
        var candles = paths.GetProperty("/api/v1/positions/{id}/candles").GetProperty("get");

        market.GetProperty("security").GetArrayLength().Should().BeGreaterThan(0);
        candles.GetProperty("security").GetArrayLength().Should().BeGreaterThan(0);
        market.GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().BeEquivalentTo(["200", "400", "401", "403", "404", "503"]);
        candles.GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().BeEquivalentTo(["200", "400", "401", "403", "404", "503"]);

        var parameters = candles.GetProperty("parameters");
        var interval = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "interval");
        interval.GetProperty("required").GetBoolean().Should().BeTrue();
        interval.GetProperty("schema").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString())
            .Should().Equal(CandleIntervalV1Codec.AllWireValues);

        var limit = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "limit")
            .GetProperty("schema");
        limit.GetProperty("minimum").GetInt32().Should().Be(1);
        limit.GetProperty("maximum").GetInt32().Should().Be(500);
        limit.GetProperty("default").GetInt32().Should().Be(200);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var derivativesProperties = schemas
            .GetProperty("PositionMarketDerivativesResponse")
            .GetProperty("properties");
        AssertNullableSchema(derivativesProperties, "nextFundingTime");
        AssertNullableSchema(derivativesProperties, "premiumVsIndexPct");

        var timeframeProperties = schemas
            .GetProperty("PositionMarketTimeframeResponse")
            .GetProperty("properties");
        AssertNullableSchema(timeframeProperties, "ema20");
        AssertNullableSchema(timeframeProperties, "rsi14");
        AssertNullableSchema(timeframeProperties, "support1");

        schemas.GetProperty("PositionCandlesResponse")
            .GetProperty("properties")
            .GetProperty("interval")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(CandleIntervalV1Codec.AllWireValues);

        var marketProperties = schemas.GetProperty("PositionMarketResponse").GetProperty("properties");
        marketProperties.EnumerateObject().Select(property => property.Name)
            .Should().Contain(
            [
                "positionId",
                "exchange",
                "symbol",
                "marketCategory",
                "capturedAt",
                "price",
                "derivatives",
                "orderBook",
                "tradeFlow",
                "m15",
                "h1",
                "h4",
                "d1",
                "sentiment",
                "tags",
            ]);
        marketProperties.EnumerateObject().Select(property => property.Name)
            .Should().NotContain("portfolio")
            .And.NotContain("assessment")
            .And.NotContain("recommendation")
            .And.NotContain("indicatorDiagnostics");
    }

    private static void AssertNullableSchema(JsonElement properties, string propertyName)
    {
        properties.TryGetProperty(propertyName, out var schema).Should().BeTrue();
        schema.GetProperty("nullable").GetBoolean().Should().BeTrue();
    }
}
