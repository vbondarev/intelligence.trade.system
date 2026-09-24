using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionTimelineSwaggerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public PositionTimelineSwaggerTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Swagger_describes_the_protected_position_timeline_contract()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var timeline = root.GetProperty("paths")
            .GetProperty("/api/v1/positions/{id}/timeline")
            .GetProperty("get");

        timeline.GetProperty("security").GetArrayLength().Should().BeGreaterThan(0);
        timeline.GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().BeEquivalentTo(["200", "400", "401", "403", "404"]);

        var parameters = timeline.GetProperty("parameters");
        var pageSize = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "pageSize")
            .GetProperty("schema");
        pageSize.GetProperty("minimum").GetInt32().Should().Be(1);
        pageSize.GetProperty("maximum").GetInt32().Should().Be(100);
        pageSize.GetProperty("default").GetInt32().Should().Be(50);

        var type = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "type");
        var style = type.TryGetProperty("style", out var styleValue)
            ? styleValue.GetString()
            : "form";
        var explode = type.TryGetProperty("explode", out var explodeValue)
            ? explodeValue.GetBoolean()
            : string.Equals(style, "form", StringComparison.Ordinal);
        style.Should().Be("form");
        explode.Should().BeTrue();
        type.GetProperty("schema").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString())
            .Should().Equal("positionChange", "evaluation", "recommendation");

        var cursor = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "cursor");
        cursor.GetProperty("description").GetString().Should().Contain("Opaque");

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var item = schemas.GetProperty("PositionTimelineItemResponse");
        AssertNullableReferenceProperty(
            item,
            "positionChange",
            "#/components/schemas/PositionTimelinePositionChangeResponse");
        AssertNullableReferenceProperty(
            item,
            "evaluation",
            "#/components/schemas/PositionTimelineEvaluationResponse");
        AssertNullableReferenceProperty(
            item,
            "recommendation",
            "#/components/schemas/PositionTimelineRecommendationResponse");
    }

    private static void AssertNullableReferenceProperty(
        JsonElement schema,
        string propertyName,
        string expectedReference)
    {
        var property = schema.GetProperty("properties").GetProperty(propertyName);
        property.GetProperty("nullable").GetBoolean().Should().BeTrue();
        property.GetProperty("allOf")
            .EnumerateArray()
            .Select(element => element.GetProperty("$ref").GetString())
            .Should().Contain(expectedReference);
    }
}
