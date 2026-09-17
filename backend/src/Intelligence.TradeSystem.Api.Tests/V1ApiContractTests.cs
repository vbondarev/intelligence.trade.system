using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class V1ApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public V1ApiContractTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Auth_me_is_a_protected_v1_route()
    {
        using var response = await _client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        using var v1Response = await _client.GetAsync("/api/v1/exchange-accounts");

        legacyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        v1Response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private enum ContractState
    {
        WaitingForReview,
    }

    private sealed record V1SerializationProbe(Guid UserId, DateTimeOffset CapturedAt);
}
