using System.Text.Json;
using Intelligence.TradeSystem.Api.Realtime.V1;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class RealtimeV1ContractTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void Client_event_names_are_stable()
    {
        Assert.Equal("exchangeAccount.updated", RealtimeEventNames.ExchangeAccountUpdated);
        Assert.Equal("portfolio.updated", RealtimeEventNames.PortfolioUpdated);
        Assert.Equal("position.updated", RealtimeEventNames.PositionUpdated);
        Assert.Equal("evaluation.updated", RealtimeEventNames.EvaluationUpdated);
    }

    [Fact]
    public void Account_invalidation_payload_has_exact_v1_shape()
    {
        var message = new ExchangeAccountUpdatedMessageV1(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new DateTimeOffset(2026, 9, 22, 4, 36, 39, 873, TimeSpan.FromHours(3)),
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var json = JsonSerializer.Serialize(message, JsonOptions);

        Assert.Equal(
            """{"eventId":"11111111-1111-1111-1111-111111111111","occurredAt":"2026-09-22T04:36:39.873+03:00","exchangeAccountId":"22222222-2222-2222-2222-222222222222"}""",
            json);
        Assert.DoesNotContain("userId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Position_and_evaluation_payloads_are_id_only_invalidations()
    {
        var positionMessage = new PositionUpdatedMessageV1(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        var evaluationMessage = new EvaluationUpdatedMessageV1(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            positionMessage.PositionId);

        using var positionJson = JsonDocument.Parse(
            JsonSerializer.Serialize(positionMessage, JsonOptions));
        using var evaluationJson = JsonDocument.Parse(
            JsonSerializer.Serialize(evaluationMessage, JsonOptions));

        Assert.Equal(
            ["eventId", "occurredAt", "positionId"],
            positionJson.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["eventId", "occurredAt", "positionId"],
            evaluationJson.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain(
            positionJson.RootElement.EnumerateObject(),
            property => property.Name == "userId");
        Assert.DoesNotContain(
            evaluationJson.RootElement.EnumerateObject(),
            property => property.Name == "userId");
    }
}
