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
            .GetProperty("/api/market-analysis/snapshot")
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
            .GetProperty("/api/market-analysis/snapshot")
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

        var exchangeIdSchema = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ExchangeId");

        exchangeIdSchema
            .GetProperty("type")
            .GetString()
            .Should().Be("string");

        exchangeIdSchema
            .GetProperty("enum")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should().Contain("Bybit");

        var marketCategorySchema = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("MarketCategory");

        marketCategorySchema
            .GetProperty("type")
            .GetString()
            .Should().Be("string");

        marketCategorySchema
            .GetProperty("enum")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should().Contain(["Spot", "Linear", "Inverse"]);
    }

    [Fact]
    public async Task Swagger_Describes_Protected_V1_Exchange_Account_Lifecycle()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var paths = root.GetProperty("paths");
        var f02Operations = new[]
        {
            paths.GetProperty("/api/v1/exchange-accounts").GetProperty("get"),
            paths.GetProperty("/api/v1/exchange-accounts").GetProperty("post"),
            paths.GetProperty("/api/v1/exchange-accounts/{id}/verify").GetProperty("post"),
            paths.GetProperty("/api/v1/exchange-accounts/{id}/credentials").GetProperty("put"),
            paths.GetProperty("/api/v1/exchange-accounts/{id}/sync").GetProperty("post"),
            paths.GetProperty("/api/v1/exchange-accounts/{id}").GetProperty("delete"),
        };
        f02Operations.Should().HaveCount(6);
        foreach (var operation in f02Operations)
            operation.GetProperty("security").GetArrayLength().Should().BeGreaterThan(0);
        var connect = f02Operations[1];
        connect.GetProperty("responses").TryGetProperty("201", out _).Should().BeTrue();
        f02Operations[2].GetProperty("responses").TryGetProperty("403", out _).Should().BeTrue();
        root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer")
            .GetProperty("scheme").GetString().Should().Be("bearer");
        paths.GetProperty("/api/market-analysis/snapshot").GetProperty("post")
            .TryGetProperty("security", out _).Should().BeFalse();

        // Documented runtime status codes must match the outcomes ExchangeAccountsController
        // actually produces (see ExchangeAccountsControllerTests for the corresponding runtime assertions).
        f02Operations[0].GetProperty("responses").TryGetProperty("200", out _).Should().BeTrue();
        f02Operations[2].GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().Contain(["200", "400", "403", "404", "409", "503"]);
        f02Operations[3].GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().Contain(["200", "400", "403", "404", "409", "503"]);
        f02Operations[4].GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().Contain(["200", "400", "404", "409", "503"]);
        f02Operations[5].GetProperty("responses").EnumerateObject().Select(x => x.Name)
            .Should().Contain(["204", "400", "404", "409"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var accountSchema = schemas.GetProperty("ExchangeAccountResponse");
        var accountProperties = accountSchema.GetProperty("properties");
        accountProperties.EnumerateObject().Select(x => x.Name)
            .Should().Equal("id", "exchange", "connectionStatus", "capabilities", "lastSyncedAt");
        var lastSyncedAtSchema = accountProperties.GetProperty("lastSyncedAt");
        lastSyncedAtSchema.TryGetProperty("nullable", out var nullable).Should().BeTrue();
        nullable.GetBoolean().Should().BeTrue();

        // Response schemas (not request bodies, which legitimately accept apiKey/apiSecret to
        // submit credentials) must never expose secrets or internal credential diagnostics.
        var responseSchemaNames = new[] { "ExchangeAccountResponse", "ExchangeAccountListResponse" };
        foreach (var schemaName in responseSchemaNames)
        {
            var responseSchemaJson = schemas.GetProperty(schemaName).GetRawText();
            responseSchemaJson.Should().NotContainAny(
                "apiKey", "apiSecret", "credentialVersion", "ciphertext", "encryptionKeyId");
            responseSchemaJson.Should().NotContainAny(
                "providerAccountId", "providerIdentity", "userID", "userId");
        }

        var listSchema = schemas.GetProperty("ExchangeAccountListResponse");
        listSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name)
            .Should().Equal("items");

        var exchangeProviderSchema = schemas.GetProperty("ExchangeProvider");
        exchangeProviderSchema.GetProperty("type").GetString().Should().Be("string");
        exchangeProviderSchema.GetProperty("enum").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("bybit");

        var statusSchema = schemas.GetProperty("ExchangeAccountStatus");
        statusSchema.GetProperty("enum").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("unknown", "connected", "unavailable", "disabled");

        var capabilitySchema = schemas.GetProperty("ExchangeAccountCapability");
        capabilitySchema.GetProperty("enum").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("readBalance", "readPositions");

        paths.EnumerateObject().Select(x => x.Name)
            .Should().NotContain(path => path.StartsWith("/api/exchange-accounts", StringComparison.Ordinal));

        var createRequestSchema = schemas.GetProperty("CreateExchangeAccountRequest");
        createRequestSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name)
            .Should().Equal("exchange", "apiKey", "apiSecret");
        createRequestSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo("exchange", "apiKey", "apiSecret");
        createRequestSchema.GetProperty("properties").GetProperty("exchange")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/ExchangeProvider");
        createRequestSchema.GetProperty("properties").EnumerateObject()
            .Should().OnlyContain(property =>
                property.Name == "exchange" || property.Name == "apiKey" || property.Name == "apiSecret");

        var rotateRequestSchema = schemas.GetProperty("RotateExchangeAccountCredentialsRequest");
        rotateRequestSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name)
            .Should().Equal("apiKey", "apiSecret");
        rotateRequestSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo("apiKey", "apiSecret");
        rotateRequestSchema.GetProperty("properties").EnumerateObject()
            .Should().OnlyContain(property => property.Name == "apiKey" || property.Name == "apiSecret");

        // F-02 is limited to lifecycle; F-03+ (positions/portfolio) v1 paths must not exist yet.
        paths.EnumerateObject().Select(x => x.Name)
            .Should().NotContain(name => name.Contains("/api/v1/positions") || name.Contains("/api/v1/portfolio"));
    }


    [Fact]
    public async Task Swagger_Is_Not_Available_Outside_Development()
    {
        using var client = _factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Authentication:Issuer", "https://identity.test");
                builder.UseSetting("Authentication:MetadataAddress", "https://identity.test/.well-known/openid-configuration");
            })
            .CreateClient();

        using var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
