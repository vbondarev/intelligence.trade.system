using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts.V1;
using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class V1OpenApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedOperations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GET /api/v1/auth/me"] = "getCurrentUser",
            ["GET /api/v1/exchange-accounts"] = "listExchangeAccounts",
            ["POST /api/v1/exchange-accounts"] = "createExchangeAccount",
            ["POST /api/v1/exchange-accounts/{id}/verify"] = "verifyExchangeAccount",
            ["PUT /api/v1/exchange-accounts/{id}/credentials"] = "rotateExchangeAccountCredentials",
            ["POST /api/v1/exchange-accounts/{id}/sync"] = "syncExchangeAccount",
            ["DELETE /api/v1/exchange-accounts/{id}"] = "disconnectExchangeAccount",
            ["GET /api/v1/exchange-accounts/{id}/portfolio"] = "getExchangeAccountPortfolio",
            ["GET /api/v1/positions"] = "listPositions",
            ["GET /api/v1/positions/{id}"] = "getPosition",
            ["GET /api/v1/positions/{id}/market"] = "getPositionMarket",
            ["GET /api/v1/positions/{id}/candles"] = "getPositionCandles",
            ["GET /api/v1/positions/{id}/evaluation"] = "getPositionEvaluation",
            ["POST /api/v1/positions/{id}/evaluation"] = "evaluatePosition",
            ["GET /api/v1/positions/{id}/timeline"] = "getPositionTimeline",
        };

    private static readonly Dictionary<string, string[]> ExpectedResponses =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["getCurrentUser"] = ["200", "401", "403"],
            ["listExchangeAccounts"] = ["200", "401", "403"],
            ["createExchangeAccount"] = ["201", "400", "401", "403", "503"],
            ["verifyExchangeAccount"] = ["200", "400", "401", "403", "404", "409", "503"],
            ["rotateExchangeAccountCredentials"] = ["200", "400", "401", "403", "404", "409", "503"],
            ["syncExchangeAccount"] = ["200", "400", "401", "403", "404", "409", "503"],
            ["disconnectExchangeAccount"] = ["204", "400", "401", "403", "404", "409"],
            ["getExchangeAccountPortfolio"] = ["200", "204", "400", "401", "403", "404"],
            ["listPositions"] = ["200", "400", "401", "403"],
            ["getPosition"] = ["200", "400", "401", "403", "404"],
            ["getPositionMarket"] = ["200", "400", "401", "403", "404", "503"],
            ["getPositionCandles"] = ["200", "400", "401", "403", "404", "503"],
            ["getPositionEvaluation"] = ["200", "204", "400", "401", "403", "404"],
            ["evaluatePosition"] = ["200", "400", "401", "403", "404", "409", "503"],
            ["getPositionTimeline"] = ["200", "400", "401", "403", "404"],
        };

    private readonly WebApplicationFactory<Program> _factory;

    public V1OpenApiContractTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task V1_openapi_has_the_approved_surface_and_stable_operation_ids()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");
        var actual = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => IsHttpMethod(operation.Name))
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .Where(operation => operation.Contains(" /api/v1/", StringComparison.Ordinal))
            .ToArray();

        actual.Should().BeEquivalentTo(ExpectedOperations.Keys);

        foreach (var expected in ExpectedOperations)
        {
            var parts = expected.Key.Split(' ', 2);
            var operation = paths.GetProperty(parts[1]).GetProperty(parts[0].ToLowerInvariant());
            operation.GetProperty("operationId").GetString().Should().Be(expected.Value);
            operation.GetProperty("security").EnumerateArray()
                .Any(requirement =>
                    requirement.ValueKind == JsonValueKind.Object
                    && requirement.TryGetProperty("Bearer", out _))
                .Should().BeTrue();
            operation.GetProperty("responses").EnumerateObject()
                .Select(response => response.Name)
                .Should().BeEquivalentTo(ExpectedResponses[expected.Value]);
        }

        paths.TryGetProperty("/hubs/v1/updates", out _).Should().BeFalse();
        foreach (var marketOperation in new[]
        {
            ("/api/market-analysis/snapshot", "post"),
            ("/api/market-analysis/{symbol}/llm-payload", "get"),
        })
        {
            paths.GetProperty(marketOperation.Item1)
                .GetProperty(marketOperation.Item2)
                .TryGetProperty("security", out _)
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task Middleware_auth_responses_do_not_promise_problem_details_bodies()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        var authOnlyOperation = paths
            .GetProperty("/api/v1/exchange-accounts")
            .GetProperty("get");
        foreach (var statusCode in new[] { "401", "403" })
        {
            authOnlyOperation.GetProperty("responses").GetProperty(statusCode)
                .TryGetProperty("content", out _)
                .Should().BeFalse();
        }

        var businessErrorOperation = paths
            .GetProperty("/api/v1/exchange-accounts/{id}/verify")
            .GetProperty("post");
        businessErrorOperation.GetProperty("responses").GetProperty("403")
            .GetProperty("content")
            .GetProperty("application/problem+json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()
            .Should().Be("#/components/schemas/ProblemDetails");
    }

    [Fact]
    public async Task V1_openapi_describes_problem_details_pagination_filters_and_wire_formats()
    {
        using var document = await GetDocumentAsync();
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        var problemDetails = schemas.GetProperty("ProblemDetails");
        problemDetails.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().Contain(["type", "title", "status", "detail", "instance", "code", "traceId"]);

        var positionList = root.GetProperty("paths")
            .GetProperty("/api/v1/positions")
            .GetProperty("get");
        var parameters = positionList.GetProperty("parameters");
        parameters.EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString())
            .Should().BeEquivalentTo(
                ["exchangeAccountId", "trackingState", "symbol", "side", "pageSize", "cursor"]);
        var pageSize = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "pageSize")
            .GetProperty("schema");
        pageSize.GetProperty("minimum").GetInt32().Should().Be(1);
        pageSize.GetProperty("maximum").GetInt32().Should().Be(100);
        pageSize.GetProperty("default").GetInt32().Should().Be(50);
        parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "cursor")
            .GetProperty("description").GetString()
            .Should().Contain("Opaque");
        parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "trackingState")
            .GetProperty("description").GetString()
            .Should().Contain("active, unknown, and stale");

        var candles = root.GetProperty("paths")
            .GetProperty("/api/v1/positions/{id}/candles")
            .GetProperty("get")
            .GetProperty("parameters");
        var interval = candles.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "interval");
        interval.GetProperty("required").GetBoolean().Should().BeTrue();
        interval.GetProperty("schema").GetProperty("enum").GetArrayLength()
            .Should().Be(CandleIntervalV1Codec.AllWireValues.Count);
        var limit = candles.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "limit")
            .GetProperty("schema");
        limit.GetProperty("minimum").GetInt32().Should().Be(1);
        limit.GetProperty("maximum").GetInt32().Should().Be(500);
        limit.GetProperty("default").GetInt32().Should().Be(200);

        var timeline = root.GetProperty("paths")
            .GetProperty("/api/v1/positions/{id}/timeline")
            .GetProperty("get")
            .GetProperty("parameters");
        timeline.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "type")
            .GetProperty("schema").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString())
            .Should().BeEquivalentTo(["positionChange", "evaluation", "recommendation"]);

        var cursorPageSchemaName = GetReferenceName(
            root.GetProperty("paths")
                .GetProperty("/api/v1/positions")
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"))!;
        var cursorPageSchema = schemas.GetProperty(cursorPageSchemaName);
        AssertNullableProperty(cursorPageSchema, "nextCursor");
        AssertDateTimeOffsetProperty(schemas.GetProperty("PositionResponse"), "firstDetectedAt");
        AssertGuidProperty(schemas.GetProperty("PositionResponse"), "id");
    }

    [Fact]
    public async Task V1_openapi_enum_values_match_runtime_camel_case_values()
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var enumTypes = typeof(ExchangeProvider).Assembly.GetTypes()
            .Where(type => type.IsEnum &&
                type.Namespace?.StartsWith(
                    "Intelligence.TradeSystem.Api.Contracts.V1.",
                    StringComparison.Ordinal) == true);
        foreach (var enumType in enumTypes)
        {
            if (!schemas.TryGetProperty(enumType.Name, out var schema))
                continue;

            var runtimeValues = Enum.GetValues(enumType)
                .Cast<object>()
                .Select(value => JsonSerializer.Serialize(value, V1JsonSerializerOptions.Default))
                .Select(value => JsonDocument.Parse(value).RootElement.GetString())
                .ToArray();
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString())
                .Should().Equal(runtimeValues);
        }
    }

    [Fact]
    public async Task V1_response_schema_graph_does_not_expose_private_or_persistence_members()
    {
        using var document = await GetDocumentAsync();
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var responseSchemaNames = root.GetProperty("paths").EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/v1/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => IsHttpMethod(operation.Name))
                .SelectMany(operation => operation.Value.GetProperty("responses").EnumerateObject()))
            .SelectMany(response => response.Value.TryGetProperty("content", out var content)
                ? content.EnumerateObject()
                    .Select(mediaType => mediaType.Value.TryGetProperty("schema", out var schema)
                        ? GetReferenceName(schema)
                        : null)
                    .Where(name => name is not null)
                : [])
            .ToHashSet(StringComparer.Ordinal);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schemaName in responseSchemaNames)
            AssertSafeSchema(schemaName!, schemas, visited);
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        using var client = _factory
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development))
            .CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static bool IsHttpMethod(string name) =>
        name is "get" or "post" or "put" or "delete" or "patch" or "head" or "options";

    private static string? GetReferenceName(JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? reference.GetString()?.Split('/').Last()
            : null;

    private static void AssertSafeSchema(
        string schemaName,
        JsonElement schemas,
        HashSet<string> visited)
    {
        if (!visited.Add(schemaName))
            return;

        var schema = schemas.GetProperty(schemaName);
        WalkSchema(schema, schemas, visited);
    }

    private static void WalkSchema(
        JsonElement schema,
        JsonElement schemas,
        HashSet<string> visited)
    {
        var forbidden = new[]
        {
            "apiSecret", "credentialVersion", "ciphertext", "encryptionKeyId",
            "providerAccountId", "providerIdentity", "versionToken", "concurrencyToken",
        };

        if (schema.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in schema.EnumerateObject())
            {
                forbidden.Should().NotContain(property.Name);
                if (property.Name == "$ref")
                {
                    var referenceName = property.Value.GetString()!.Split('/').Last();
                    if (visited.Add(referenceName))
                        WalkSchema(schemas.GetProperty(referenceName), schemas, visited);
                }
                else
                {
                    WalkSchema(property.Value, schemas, visited);
                }
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
                WalkSchema(item, schemas, visited);
        }
    }

    private static void AssertNullableProperty(JsonElement schema, string propertyName)
    {
        var property = schema.GetProperty("properties").GetProperty(propertyName);
        (property.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean()
            || property.TryGetProperty("type", out var type) &&
                type.GetString()?.Contains("null", StringComparison.Ordinal) == true)
            .Should().BeTrue();
    }

    private static void AssertDateTimeOffsetProperty(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName)
            .GetProperty("format").GetString().Should().Be("date-time");

    private static void AssertGuidProperty(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName)
            .GetProperty("format").GetString().Should().Be("uuid");
}
