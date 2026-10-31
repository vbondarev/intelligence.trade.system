using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitPublicMarkPriceProviderTests
{
    [Fact]
    public async Task Subscribes_to_the_public_linear_ticker_and_normalizes_updates()
    {
        using var harness = new BybitSocketTestHarness(authenticated: false);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var provider = new BybitPublicMarkPriceProvider(
            harness.Client,
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());
        var observations = new List<MarketMarkPriceObservation>();

        await using var subscription = await provider.SubscribeAsync(
            InstrumentId.From("BTCUSDT"),
            observations.Add,
            () => { });
        harness.Push(
            """{"topic":"tickers.BTCUSDT","type":"snapshot","data":{"symbol":"BTCUSDT","markPrice":"17217.33"},"cs":1,"ts":1673272861686}""");
        harness.Push(
            """{"topic":"tickers.BTCUSDT","type":"delta","data":{"symbol":"BTCUSDT","lastPrice":"1"},"cs":2,"ts":1673272861786}""");

        observations.Should().HaveCount(2);
        observations[0].MarkPrice.Should().Be(17_217.33m);
        observations[0].UpdateKind.Should().Be(MarketPriceUpdateKind.Snapshot);
        observations[1].MarkPrice.Should().BeNull();
        observations[1].UpdateKind.Should().Be(MarketPriceUpdateKind.Delta);
        harness.SentMessages.Should().Contain(message => message.Contains("tickers.BTCUSDT", StringComparison.Ordinal));
        harness.SentMessages.Should().NotContain(message => message.Contains("\"op\":\"auth\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_a_rejected_public_ticker_subscription()
    {
        using var harness = new BybitSocketTestHarness(
            authenticated: false,
            subscribeSucceeds: false);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var provider = new BybitPublicMarkPriceProvider(
            harness.Client,
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());

        var act = () => provider.SubscribeAsync(
            InstrumentId.From("BTCUSDT"),
            _ => { },
            () => { });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rejects_an_uninitialized_symbol_before_opening_a_subscription()
    {
        using var harness = new BybitSocketTestHarness(authenticated: false);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var provider = new BybitPublicMarkPriceProvider(
            harness.Client,
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());

        var act = () => provider.SubscribeAsync(default, _ => { }, () => { });

        await act.Should().ThrowAsync<ArgumentException>();
        harness.SentMessages.Should().BeEmpty();
    }
}
