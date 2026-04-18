using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class SwaggerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SwaggerEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Swagger_Is_Available_In_Development()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Swagger UI");
    }

    [Fact]
    public async Task Swagger_Describes_Snapshot_Response_Using_Public_MarketAnalysisResponse_Contract()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var snapshotPost = root
            .GetProperty("paths")
            .GetProperty("/api/analysis/snapshot")
            .GetProperty("post");

        var schemaReference = snapshotPost
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        schemaReference.Should().Be("#/components/schemas/MarketAnalysisResponse");

        var responseSchemaProperties = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("MarketAnalysisResponse")
            .GetProperty("properties");

        responseSchemaProperties.TryGetProperty("marketData", out _).Should().BeFalse();
        responseSchemaProperties.TryGetProperty("m15", out _).Should().BeTrue();
        responseSchemaProperties.TryGetProperty("h1", out _).Should().BeTrue();
        responseSchemaProperties.TryGetProperty("h4", out _).Should().BeTrue();
        responseSchemaProperties.TryGetProperty("d1", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Swagger_Includes_Xml_Comments_For_Actions_And_Dtos()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        var snapshotPost = root
            .GetProperty("paths")
            .GetProperty("/api/analysis/snapshot")
            .GetProperty("post");

        snapshotPost
            .GetProperty("summary")
            .GetString()
            .Should().Be("Строит рыночный снимок по указанному инструменту.");

        var marketAnalysisResponseSchema = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("MarketAnalysisResponse");

        marketAnalysisResponseSchema
            .GetProperty("description")
            .GetString()
            .Should().Be("Ответ API с агрегированным рыночным снимком инструмента.");

        marketAnalysisResponseSchema
            .GetProperty("properties")
            .GetProperty("exchange")
            .GetProperty("description")
            .GetString()
            .Should().Be("Название биржи, с которой был собран снимок.");

        var aiAnalysisRequestSchema = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("AiAnalysisRequest");

        aiAnalysisRequestSchema
            .GetProperty("description")
            .GetString()
            .Should().Be("Запрос API на построение AI-анализа по указанному инструменту.");

        aiAnalysisRequestSchema
            .GetProperty("properties")
            .GetProperty("userQuery")
            .GetProperty("description")
            .GetString()
            .Should().Be("Пользовательский запрос, который передаётся AI-сервису вместе с рыночным снимком.");
    }

    [Fact]
    public async Task Swagger_Is_Not_Available_Outside_Development()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

