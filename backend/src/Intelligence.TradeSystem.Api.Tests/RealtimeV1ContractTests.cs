using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Realtime.V1;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class RealtimeV1ContractTests(
    ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public void Client_event_names_are_stable()
    {
        Assert.Equal("exchangeAccount.updated", RealtimeEventNames.ExchangeAccountUpdated);
        Assert.Equal("portfolio.updated", RealtimeEventNames.PortfolioUpdated);
        Assert.Equal("position.updated", RealtimeEventNames.PositionUpdated);
        Assert.Equal("evaluation.updated", RealtimeEventNames.EvaluationUpdated);
    }

    [Theory]
    [MemberData(nameof(RealtimePayloads))]
    public void SignalR_protocol_serializes_each_v1_event_with_the_exact_wire_contract(
        string eventName,
        object payload,
        string expectedPayloadJson,
        string expectedResourceProperty)
    {
        var protocol = new JsonHubProtocol(
            factory.Services.GetRequiredService<IOptions<JsonHubProtocolOptions>>());
        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(
            new InvocationMessage(
                "contract-test",
                eventName,
                new object[] { payload }),
            buffer);

        buffer.WrittenSpan[^1].Should().Be(0x1e);
        var wireJson = Encoding.UTF8.GetString(buffer.WrittenSpan[..^1]);
        var expectedWireJson =
            $$"""{"type":1,"invocationId":"contract-test","target":"{{eventName}}","arguments":[{{expectedPayloadJson}}]}""";
        Assert.Equal(expectedWireJson, wireJson);

        using var document = JsonDocument.Parse(wireJson);
        Assert.Equal(eventName, document.RootElement.GetProperty("target").GetString());
        Assert.Equal(
            expectedPayloadJson,
            document.RootElement
                .GetProperty("arguments")[0]
                .GetRawText());
        Assert.Equal(
            new[] { expectedResourceProperty, "eventId", "occurredAt" }
                .OrderBy(name => name, StringComparer.Ordinal),
            document.RootElement
                .GetProperty("arguments")[0]
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(
            document.RootElement.GetProperty("arguments")[0].EnumerateObject(),
            property => property.Name == "userId");
    }

    public static IEnumerable<object[]> RealtimePayloads()
    {
        const string occurredAt = "2026-09-22T04:36:39.873+03:00";
        yield return
        [
            RealtimeEventNames.ExchangeAccountUpdated,
            new ExchangeAccountUpdatedMessageV1(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
                Guid.Parse("22222222-2222-2222-2222-222222222222")),
            $$"""{"eventId":"11111111-1111-1111-1111-111111111111","occurredAt":"{{occurredAt}}","exchangeAccountId":"22222222-2222-2222-2222-222222222222"}""",
            "exchangeAccountId",
        ];
        yield return
        [
            RealtimeEventNames.PortfolioUpdated,
            new PortfolioUpdatedMessageV1(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
                Guid.Parse("44444444-4444-4444-4444-444444444444")),
            $$"""{"eventId":"33333333-3333-3333-3333-333333333333","occurredAt":"{{occurredAt}}","exchangeAccountId":"44444444-4444-4444-4444-444444444444"}""",
            "exchangeAccountId",
        ];
        yield return
        [
            RealtimeEventNames.PositionUpdated,
            new PositionUpdatedMessageV1(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
                Guid.Parse("66666666-6666-6666-6666-666666666666")),
            $$"""{"eventId":"55555555-5555-5555-5555-555555555555","occurredAt":"{{occurredAt}}","positionId":"66666666-6666-6666-6666-666666666666"}""",
            "positionId",
        ];
        yield return
        [
            RealtimeEventNames.EvaluationUpdated,
            new EvaluationUpdatedMessageV1(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
                Guid.Parse("88888888-8888-8888-8888-888888888888")),
            $$"""{"eventId":"77777777-7777-7777-7777-777777777777","occurredAt":"{{occurredAt}}","positionId":"88888888-8888-8888-8888-888888888888"}""",
            "positionId",
        ];
    }
}
