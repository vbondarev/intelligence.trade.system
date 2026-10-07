using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts.V1;
using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class V1OpenApiContractTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedOperations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GET /api/v1/auth/me"] = "getCurrentUser",
            ["GET /api/v1/me/exchange-accounts"] = "listExchangeAccounts",
            ["POST /api/v1/me/exchange-accounts"] = "createExchangeAccount",
            ["PATCH /api/v1/me/exchange-accounts/{id}"] = "renameExchangeAccount",
            ["POST /api/v1/me/exchange-accounts/{id}/verify"] = "verifyExchangeAccount",
            ["PUT /api/v1/me/exchange-accounts/{id}/credentials"] = "rotateExchangeAccountCredentials",
            ["DELETE /api/v1/me/exchange-accounts/{id}"] = "disconnectExchangeAccount",
            ["POST /api/v1/exchange-accounts/{id}/sync"] = "syncExchangeAccount",
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
            ["createExchangeAccount"] = ["200", "201", "400", "401", "403", "409", "503"],
            ["renameExchangeAccount"] = ["200", "400", "401", "403", "404", "409"],
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

    private static readonly HashSet<string> BusinessForbiddenOperationIds =
    [
        "createExchangeAccount",
        "verifyExchangeAccount",
        "rotateExchangeAccountCredentials",
    ];

    private readonly ApiWebApplicationFactory _factory;

    public V1OpenApiContractTests(ApiWebApplicationFactory factory) => _factory = factory;

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

        var actualOperationIds = ExpectedOperations.Keys
            .Select(expected =>
            {
                var parts = expected.Split(' ', 2);
                return paths.GetProperty(parts[1])
                    .GetProperty(parts[0].ToLowerInvariant())
                    .GetProperty("operationId")
                    .GetString();
            })
            .ToArray();
        actualOperationIds.Should().HaveCount(ExpectedOperations.Count);
        actualOperationIds.Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id));
        actualOperationIds.Should().OnlyHaveUniqueItems();
        ExpectedOperations.Values.Should().OnlyHaveUniqueItems();

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
    public async Task V1_auth_responses_describe_problem_details_bodies_and_bearer_challenge()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        foreach (var expected in ExpectedOperations)
        {
            var parts = expected.Key.Split(' ', 2);
            var operation = paths.GetProperty(parts[1]).GetProperty(parts[0].ToLowerInvariant());
            var responses = operation.GetProperty("responses");
            var unauthorized = responses.GetProperty("401");
            unauthorized.GetProperty("description").GetString()
                .Should().Contain("authentication_required");
            unauthorized.GetProperty("description").GetString()
                .Should().Contain("Authentication required.");
            unauthorized.GetProperty("content")
                .GetProperty("application/problem+json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
                .Should().Be("#/components/schemas/ProblemDetails");
            unauthorized.GetProperty("headers")
                .GetProperty("WWW-Authenticate")
                .GetProperty("schema")
                .GetProperty("type")
                .GetString()
                .Should().Be("string");

            var forbidden = responses.GetProperty("403");
            forbidden.GetProperty("description").GetString()
                .Should().Contain("access_forbidden");
            forbidden.GetProperty("description").GetString()
                .Should().Contain("Access forbidden.");
            forbidden.GetProperty("content")
                .GetProperty("application/problem+json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
                .Should().Be("#/components/schemas/ProblemDetails");
            if (BusinessForbiddenOperationIds.Contains(expected.Value))
            {
                forbidden.GetProperty("description").GetString()
                    .Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task V1_create_exchange_account_documents_create_reconnect_and_duplicate_responses()
    {
        using var document = await GetDocumentAsync();
        var responses = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/me/exchange-accounts")
            .GetProperty("post")
            .GetProperty("responses");

        foreach (var status in new[] { "200", "201" })
            GetResponseSchemaNames(responses, status).Should().NotBeEmpty().And.OnlyContain(name => name == "ExchangeAccountResponse");

        GetResponseSchemaNames(responses, "409").Should().NotBeEmpty().And.OnlyContain(name => name == "ProblemDetails");
    }

    [Fact]
    public async Task V1_rename_exchange_account_documents_the_request_and_response_contract()
    {
        using var document = await GetDocumentAsync();
        var root = document.RootElement;
        var operation = root.GetProperty("paths")
            .GetProperty("/api/v1/me/exchange-accounts/{id}")
            .GetProperty("patch");
        var responses = operation.GetProperty("responses");

        GetResponseSchemaNames(responses, "200").Should().NotBeEmpty()
            .And.OnlyContain(name => name == "ExchangeAccountResponse");
        foreach (var status in new[] { "400", "404", "409" })
            GetResponseSchemaNames(responses, status).Should().NotBeEmpty().And.OnlyContain(name => name == "ProblemDetails");

        var requestSchemaName = GetReferenceName(operation.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema"));
        requestSchemaName.Should().Be("RenameExchangeAccountRequest");
        var requestSchema = root.GetProperty("components").GetProperty("schemas").GetProperty(requestSchemaName!);
        requestSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["displayName"]);
        requestSchema.GetProperty("required").EnumerateArray().Select(property => property.GetString())
            .Should().Contain("displayName");
    }

    [Theory]
    [InlineData(nameof(CreateExchangeAccountRequest))]
    [InlineData(nameof(RenameExchangeAccountRequest))]
    public async Task V1_exchange_account_requests_publish_required_display_name_with_the_domain_max_length(
        string schemaName)
    {
        using var document = await GetDocumentAsync();
        var requestSchema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);

        requestSchema.GetProperty("required").EnumerateArray().Select(property => property.GetString())
            .Should().Contain("displayName");
        requestSchema.GetProperty("properties").GetProperty("displayName").GetProperty("maxLength").GetInt32()
            .Should().Be(ExchangeAccount.DisplayNameMaxLength);
    }

    [Fact]
    public async Task V1_openapi_does_not_document_former_management_routes()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/exchange-accounts", out _).Should().BeFalse();
        paths.TryGetProperty("/api/v1/exchange-accounts/{id}", out _).Should().BeFalse();
        paths.TryGetProperty("/api/v1/exchange-accounts/{id}/verify", out _).Should().BeFalse();
        paths.TryGetProperty("/api/v1/exchange-accounts/{id}/credentials", out _).Should().BeFalse();
        paths.TryGetProperty("/api/v1/me/exchange-accounts/{id}/sync", out _).Should().BeFalse();
    }

    [Fact]
    public async Task V1_openapi_describes_problem_details_pagination_filters_and_wire_formats()
    {
        using var document = await GetDocumentAsync();
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        var problemDetails = schemas.GetProperty("ProblemDetails");
        problemDetails.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().Contain(["type", "title", "status", "detail", "instance", "code", "traceId", "reason"]);
        var reasonProperty = problemDetails.GetProperty("properties").GetProperty("reason");
        GetReferenceName(reasonProperty)!.Should().Be("PositionNotEvaluableReasonV1");
        (!problemDetails.TryGetProperty("required", out var required)
            || required.EnumerateArray()
                .All(property => property.GetString() != "reason"))
            .Should()
            .BeTrue();
        schemas.GetProperty("PositionNotEvaluableReasonV1")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .Equal(
                "closedPosition",
                "portfolioUnavailable",
                "portfolioInconsistent",
                "temporalInconsistency");

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
    public async Task V1_openapi_describes_settlement_asset_and_grouped_portfolio_exposures()
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var positionSchema in new[] { "PositionResponse", "PositionListItemResponse" })
        {
            schemas.GetProperty(positionSchema).GetProperty("properties")
                .GetProperty("settlementAsset").GetProperty("type").GetString()
                .Should().Contain("string");
        }

        var portfolioProperties = schemas.GetProperty("PortfolioResponse").GetProperty("properties");
        portfolioProperties.GetProperty("currentPositionCount").GetProperty("type").GetString()
            .Should().Contain("integer");
        var exposures = portfolioProperties.GetProperty("exposures");
        exposures.GetProperty("type").GetString().Should().Contain("array");
        GetReferenceName(exposures.GetProperty("items")).Should().Be("PortfolioExposureResponse");

        var exposureSchema = schemas.GetProperty("PortfolioExposureResponse");
        exposureSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("settlementAsset", "grossExposure", "longExposure", "shortExposure");
        AssertNullableProperty(exposureSchema, "grossExposure");
        AssertNullableProperty(exposureSchema, "longExposure");
        AssertNullableProperty(exposureSchema, "shortExposure");
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

    private static string?[] GetResponseSchemaNames(JsonElement responses, string status) =>
        responses.GetProperty(status)
            .GetProperty("content")
            .EnumerateObject()
            .Select(mediaType => GetReferenceName(mediaType.Value.GetProperty("schema")))
            .ToArray();

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
