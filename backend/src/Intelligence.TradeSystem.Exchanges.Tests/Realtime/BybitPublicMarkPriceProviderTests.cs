using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using CryptoExchange.Net.Objects.Sockets;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitPublicMarkPriceProviderTests
{
    [Fact]
    public async Task Does_not_retry_an_unconfirmed_close_or_report_it_as_successful()
    {
        var closeCalls = 0;
        var exception = new InvalidOperationException("close failed");
        var lease = new BybitPublicMarkPriceSubscriptionLease(() =>
        {
            closeCalls++;
            throw exception;
        });

        var firstAttempt = () => lease.DisposeAsync().AsTask();
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("close failed");

        var secondAttempt = () => lease.DisposeAsync().AsTask();
        await secondAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("close failed");

        closeCalls.Should().Be(1);
    }

    private static IBybitSocketClient CreateClientReturning(
        WebSocketResult<UpdateSubscription> result)
    {
        var linearApi = new Mock<IBybitSocketClientLinearApi>();
        linearApi
            .Setup(api => api.SubscribeToTickerUpdatesAsync(
                It.IsAny<string>(),
                It.IsAny<Action<DataEvent<BybitLinearTickerUpdate>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var client = new Mock<IBybitSocketClient>();
        client.SetupGet(socketClient => socketClient.V5LinearApi)
            .Returns(linearApi.Object);
        return client.Object;
    }

    [Fact]
    public async Task Serializes_concurrent_close_calls_and_treats_success_as_idempotent()
    {
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeCalls = 0;
        var activeCloseCalls = 0;
        var maximumConcurrentCloseCalls = 0;
        var lease = new BybitPublicMarkPriceSubscriptionLease(async () =>
        {
            Interlocked.Increment(ref closeCalls);
            var concurrentCalls = Interlocked.Increment(ref activeCloseCalls);
            UpdateMaximum(ref maximumConcurrentCloseCalls, concurrentCalls);
            closeStarted.TrySetResult();
            try
            {
                await releaseClose.Task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref activeCloseCalls);
            }
        });

        var firstDispose = lease.DisposeAsync().AsTask();
        await closeStarted.Task;
        var secondDispose = lease.DisposeAsync().AsTask();

        closeCalls.Should().Be(1);
        secondDispose.IsCompleted.Should().BeFalse();

        releaseClose.TrySetResult();
        await Task.WhenAll(firstDispose, secondDispose);
        await lease.DisposeAsync();

        closeCalls.Should().Be(1);
        maximumConcurrentCloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task Keeps_an_asynchronously_failed_close_unconfirmed_without_retrying()
    {
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeCalls = 0;
        var exception = new InvalidOperationException("asynchronous close failed");
        var lease = new BybitPublicMarkPriceSubscriptionLease(async () =>
        {
            Interlocked.Increment(ref closeCalls);
            closeStarted.TrySetResult();
            await failClose.Task.ConfigureAwait(false);
            throw exception;
        });

        var firstDispose = lease.DisposeAsync().AsTask();
        await closeStarted.Task;
        firstDispose.IsCompleted.Should().BeFalse();
        failClose.TrySetResult();

        var firstAttempt = () => firstDispose;
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("asynchronous close failed");

        var secondAttempt = () => lease.DisposeAsync().AsTask();
        await secondAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("asynchronous close failed");

        closeCalls.Should().Be(1);
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var current = Volatile.Read(ref maximum);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

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

        var exception = await act.Should()
            .ThrowAsync<MarketPriceSubscriptionStartUnconfirmedException>();
        exception.Which.UnconfirmedLease.Should().BeNull();
    }

    [Fact]
    public async Task Preserves_uncertainty_when_the_sdk_internal_cleanup_fails()
    {
        using var harness = new BybitSocketTestHarness(
            authenticated: false,
            subscribeSucceeds: false);
        harness.FailClose = true;
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var provider = new BybitPublicMarkPriceProvider(
            harness.Client,
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());

        var act = () => provider.SubscribeAsync(
            InstrumentId.From("BTCUSDT"),
            _ => { },
            () => { });

        var exception = await act.Should()
            .ThrowAsync<MarketPriceSubscriptionStartUnconfirmedException>();
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        exception.Which.UnconfirmedLease.Should().BeNull();
        harness.CloseAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Treats_success_without_subscription_data_as_unconfirmed()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var client = CreateClientReturning(
            new WebSocketResult<UpdateSubscription>("Bybit", null, null));
        await using var provider = new BybitPublicMarkPriceProvider(
            client,
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());

        var act = () => provider.SubscribeAsync(
            InstrumentId.From("BTCUSDT"),
            _ => { },
            () => { });

        var exception = await act.Should()
            .ThrowAsync<MarketPriceSubscriptionStartUnconfirmedException>();
        exception.Which.UnconfirmedLease.Should().BeNull();
    }

    [Fact]
    public async Task Closes_a_rejected_subscription_before_returning_a_retryable_failure()
    {
        using var harness = new BybitSocketTestHarness(authenticated: false);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var subscription = await harness.Client.V5LinearApi.SubscribeToTickerUpdatesAsync(
            "BTCUSDT",
            _ => { });
        var result = new WebSocketResult<UpdateSubscription>(
            "Bybit",
            subscription.Data,
            new ServerError(ErrorType.Unknown, "test failure", null!)
            {
                Message = "test failure",
            });
        await using var provider = new BybitPublicMarkPriceProvider(
            CreateClientReturning(result),
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());

        var act = () => provider.SubscribeAsync(
            InstrumentId.From("BTCUSDT"),
            _ => { },
            () => { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Bybit public mark-price subscription failed.");
        harness.CloseAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Returns_the_unconfirmed_lease_when_a_rejected_subscription_cannot_be_closed()
    {
        using var harness = new BybitSocketTestHarness(authenticated: false);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var subscription = await harness.Client.V5LinearApi.SubscribeToTickerUpdatesAsync(
            "BTCUSDT",
            _ => { });
        harness.FailClose = true;
        var result = new WebSocketResult<UpdateSubscription>(
            "Bybit",
            subscription.Data,
            new ServerError(ErrorType.Unknown, "test failure", null!)
            {
                Message = "test failure",
            });
        await using var provider = new BybitPublicMarkPriceProvider(
            CreateClientReturning(result),
            loggerFactory.CreateLogger<BybitPublicMarkPriceProvider>());

        var act = () => provider.SubscribeAsync(
            InstrumentId.From("BTCUSDT"),
            _ => { },
            () => { });

        var exception = await act.Should()
            .ThrowAsync<MarketPriceSubscriptionStartUnconfirmedException>();
        exception.Which.UnconfirmedLease.Should().NotBeNull();
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        harness.CloseAttempts.Should().Be(1);
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
