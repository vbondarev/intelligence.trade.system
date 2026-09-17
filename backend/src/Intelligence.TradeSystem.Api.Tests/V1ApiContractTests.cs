using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Testing;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class V1ApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public V1ApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Auth_me_is_a_protected_v1_route()
    {
        using var response = await _client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_only_serialization_route_is_not_in_the_production_surface()
    {
        using var response = await _client.GetAsync("/api/v1/test-only/serialization");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task V1_mvc_boundary_applies_the_v1_json_contract()
    {
        using var client = CreateSerializationClient();
        using var response = await client.GetAsync("/api/v1/test-only/serialization");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var item = root.GetProperty("items").EnumerateArray().Single();

        root.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .Equal("items", "nextCursor", "hasMore");
        item.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .Equal("id", "state", "optionalValue", "capturedAt");
        item.GetProperty("id").GetString()
            .Should()
            .Be("2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6");
        item.GetProperty("state").GetString().Should().Be("waitingForReview");
        item.GetProperty("optionalValue").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("capturedAt").GetString()
            .Should()
            .Be("2026-09-16T17:13:04+03:00");
        root.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/json")]
    [InlineData("application/vnd.intelligence-trade+json")]
    public async Task V1_mvc_boundary_keeps_the_same_contract_for_json_media_types(
        string mediaType)
    {
        using var client = CreateSerializationClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/test-only/serialization");
        request.Headers.Accept.ParseAdd(mediaType);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await ReadSerializationItemAsync(response);
        item.GetProperty("id").GetString()
            .Should()
            .Be("2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6");
        item.GetProperty("state").GetString().Should().Be("waitingForReview");
        item.GetProperty("optionalValue").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("capturedAt").GetString()
            .Should()
            .Be("2026-09-16T17:13:04+03:00");
    }

    [Fact]
    public async Task V1_mvc_boundary_rejects_unsupported_non_json_media_types()
    {
        using var client = CreateSerializationClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/test-only/serialization");
        request.Headers.Accept.ParseAdd("text/plain");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);
    }

    [Fact]
    public async Task Legacy_mvc_boundary_keeps_existing_enum_wire_values()
    {
        using var client = CreateSerializationClient();
        using var response = await client.GetAsync("/test-only/legacy-serialization");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();

        item.GetProperty("state").GetString().Should().Be("WaitingForReview");
    }

    [Fact]
    public async Task V1_openapi_enum_matches_the_runtime_wire_value()
    {
        using var client = _factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(V1SerializationTestController).Assembly));
            })
            .CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stateSchema = json.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ContractState");
        var enumValues = stateSchema
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        enumValues.Should().ContainSingle("waitingForReview");
        enumValues.Should().NotContain("WaitingForReview");
    }

    [Fact]
    public async Task V1_mvc_boundary_preserves_the_existing_problem_details_contract()
    {
        using var client = CreateSerializationClient();
        using var response = await client.GetAsync(
            "/api/v1/test-only/serialization/problem");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should()
            .Be("application/problem+json");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("type").GetString()
            .Should()
            .Be("urn:intelligence-trade:error:validation-failed");
        root.GetProperty("title").GetString().Should().Be("Request validation failed.");
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        root.GetProperty("detail").GetString().Should().Be("Test validation problem.");
        root.GetProperty("instance").GetString()
            .Should()
            .Be("/api/v1/test-only/serialization/problem");
        root.GetProperty("code").GetString().Should().Be("validation_failed");
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Pre_v1_exchange_accounts_route_remains_protected_without_a_v1_alias()
    {
        using var legacyResponse = await _client.PostAsJsonAsync(
            "/api/exchange-accounts/bybit",
            new
            {
                apiKey = "api-key",
                apiSecret = "api-secret",
            });
        using var v1ListResponse = await _client.GetAsync("/api/v1/exchange-accounts");
        using var v1ConnectResponse = await _client.PostAsJsonAsync(
            "/api/v1/exchange-accounts/bybit",
            new
            {
                apiKey = "api-key",
                apiSecret = "api-secret",
            });

        legacyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        v1ListResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        v1ConnectResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Public_market_analysis_remains_outside_v1()
    {
        using var legacySnapshotResponse = await _client.PostAsync("/api/market-analysis/snapshot", content: null);
        using var legacyLlmResponse = await _client.PostAsync("/api/market-analysis/BTCUSDT/llm-payload", content: null);
        using var v1SnapshotResponse = await _client.PostAsync("/api/v1/market-analysis/snapshot", content: null);
        using var v1LlmResponse = await _client.GetAsync("/api/v1/market-analysis/BTCUSDT/llm-payload");

        legacySnapshotResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        legacyLlmResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        v1SnapshotResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        v1LlmResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Problem_details_uses_the_stable_error_contract()
    {
        using var response = await _client.PostAsync("/api/market-analysis/snapshot", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should()
            .Be("application/problem+json");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .Contain(["type", "title", "status", "detail", "instance", "code", "traceId"]);
        root.GetProperty("type").GetString()
            .Should()
            .Be("urn:intelligence-trade:error:validation-failed");
        root.GetProperty("title").GetString().Should().Be("Request validation failed.");
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        root.GetProperty("detail").GetString().Should().Be("Snapshot request body is required.");
        root.GetProperty("instance").GetString().Should().Be("/api/market-analysis/snapshot");
        root.GetProperty("code").GetString().Should().Be("validation_failed");
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void V1_json_contract_preserves_nullable_cursor_and_uses_camel_case_values()
    {
        var page = new CursorPage<ContractState>(
            [ContractState.WaitingForReview],
            NextCursor: null,
            HasMore: false);

        using var pageJson = JsonDocument.Parse(
            JsonSerializer.Serialize(page, V1JsonSerializerOptions.Default));
        var pageElement = pageJson.RootElement;

        pageElement.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .Equal("items", "nextCursor", "hasMore");
        pageElement.GetProperty("items")[0].GetString().Should().Be("waitingForReview");
        pageElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        pageElement.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void V1_json_contract_serializes_guid_and_offset_timestamp_as_strings()
    {
        var userId = Guid.Parse("2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6");
        var capturedAt = new DateTimeOffset(
            2026,
            9,
            16,
            17,
            13,
            4,
            TimeSpan.FromHours(3));
        var payload = new V1SerializationProbe(userId, capturedAt);

        var serialize = JsonSerializer.Serialize(payload, V1JsonSerializerOptions.Default);
        using var json = JsonDocument.Parse(serialize);
        var root = json.RootElement;

        root.GetProperty("userId").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("userId").GetString().Should().Be(userId.ToString("D"));
        root.GetProperty("userId").GetGuid().Should().Be(userId);
        root.GetProperty("capturedAt").ValueKind.Should().Be(JsonValueKind.String);
        DateTimeOffset.Parse(
                root.GetProperty("capturedAt").GetString()!,
                CultureInfo.InvariantCulture)
            .Offset.Should()
            .Be(TimeSpan.FromHours(3));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void Cursor_page_size_uses_shared_v1_bounds(int? pageSize, bool expected)
    {
        CursorPagination.IsValidPageSize(pageSize).Should().Be(expected);
    }

    private HttpClient CreateSerializationClient() =>
        _factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(V1SerializationTestController).Assembly)))
            .CreateClient();

    private static async Task<JsonElement> ReadSerializationItemAsync(
        HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Single().Clone();
    }

    private enum ContractState
    {
        WaitingForReview,
    }

    private sealed record V1SerializationProbe(Guid UserId, DateTimeOffset CapturedAt);
}

[ApiController]
[Route("api/v1/test-only/serialization")]
[Route("test-only/legacy-serialization")]
public sealed class V1SerializationTestController : ControllerBase
{
    private static readonly Guid ItemId =
        Guid.Parse("2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6");
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 16, 17, 13, 4, TimeSpan.FromHours(3));

    [HttpGet]
    public ActionResult<CursorPage<V1SerializationItem>> Get() =>
        Ok(new CursorPage<V1SerializationItem>(
            [new V1SerializationItem(ItemId, ContractState.WaitingForReview, null, CapturedAt)],
            NextCursor: null,
            HasMore: false));

    [HttpGet("problem")]
    public IActionResult GetProblem() =>
        BadRequest(ApiProblemDetails.CreateValidation(HttpContext, "Test validation problem."));
}
