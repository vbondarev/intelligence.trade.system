using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects.Sockets;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using Microsoft.Extensions.Logging;
using static Intelligence.TradeSystem.Exchanges.Tests.Realtime.BybitRealtimeTestData;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitRealtimeProviderTests
{
    private const string ApiKey = "realtime-api-key-secret-value";
    private const string ApiSecret = "realtime-api-secret-secret-value";
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PendingProbe = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task StartAsync_Subscribes_Private_Streams_And_Delivers_Normalized_Observations()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);

        var state = await provider.StartAsync(observer, CancellationToken.None);

        state.Status.Should().Be(RealtimeTransportStatus.Subscribed);
        state.RecoveryRequired.Should().BeFalse();
        state.Failure.Should().BeNull();
        harness.Socket.IsOpen.Should().BeTrue();
        harness.SentMessages.Should().Contain(message => message.Contains("\"subscribe\"") && message.Contains("\"position\""));
        harness.SentMessages.Should().Contain(message => message.Contains("\"subscribe\"") && message.Contains("\"wallet\""));
        harness.SentMessages.Should().NotContain(message => message.Contains("order", StringComparison.OrdinalIgnoreCase));

        harness.Push(PositionMessage(PositionItem()));
        harness.Push(WalletMessage());

        var position = observer.Positions.Should().ContainSingle().Subject;
        position.Kind.Should().Be(PrivatePositionObservationKind.Open);
        position.ExchangeAccountId.Should().Be(provider.ExchangeAccountId);
        position.SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        observer.Wallets.Should().ContainSingle().Which.ExchangeAccountId.Should().Be(provider.ExchangeAccountId);
        observer.TransportStates.Should().BeEmpty();

        await provider.StopAsync();
    }

    [Fact]
    public async Task StartAsync_Maps_Authentication_Failure_To_Non_Retryable_Invalid_Credentials()
    {
        using var harness = new BybitSocketTestHarness(authSucceeds: false);
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);

        var state = await provider.StartAsync(observer, CancellationToken.None);

        state.Status.Should().Be(RealtimeTransportStatus.SubscribeFailed);
        state.RecoveryRequired.Should().BeTrue();
        state.Failure.Should().BeEquivalentTo(new ExchangeFailure(ExchangeFailureKind.InvalidCredentials, Retryable: false));
        harness.SentMessages.Should().NotContain(message => message.Contains("\"subscribe\""));
    }

    [Fact]
    public async Task StartAsync_Closes_Position_Subscription_When_Wallet_Subscription_Is_Rejected()
    {
        using var harness = new BybitSocketTestHarness { RejectedTopic = "wallet" };
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);

        var state = await provider.StartAsync(observer, CancellationToken.None);

        state.Status.Should().Be(RealtimeTransportStatus.SubscribeFailed);
        state.Failure.Should().NotBeNull();
        state.Failure!.Kind.Should().Be(ExchangeFailureKind.Unknown);
        // Закрытие последней подписки соединения SDK выполняет закрытием самого websocket.
        await WaitUntilAsync(() => !harness.Socket.IsOpen);
        observer.Positions.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_Propagates_Cancellation_Before_Subscribing()
    {
        using var harness = new BybitSocketTestHarness();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var provider = CreateProvider(harness);

        var act = () => provider.StartAsync(new RecordingObserver(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.SentMessages.Should().NotContain(message => message.Contains("\"subscribe\""));
    }

    [Fact]
    public async Task StartAsync_Propagates_Cancellation_During_Subscription()
    {
        using var harness = new BybitSocketTestHarness();
        var resolver = InstrumentMetadata.Resolver(("BTCUSDT", "USDT"));
        (await resolver.EnsureLoadedAsync()).Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness, resolver);

        var act = () => provider.StartAsync(observer, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.SentMessages.Should().NotContain(message => message.Contains("\"subscribe\""));
        observer.TransportStates.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_Can_Be_Called_Only_Once()
    {
        using var harness = new BybitSocketTestHarness();
        var firstObserver = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(firstObserver, CancellationToken.None);

        var act = () => provider.StartAsync(new RecordingObserver(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Push(PositionMessage(PositionItem()));
        firstObserver.Positions.Should().ContainSingle();
        await provider.StopAsync();
    }

    [Fact]
    public async Task StartAsync_After_Stop_Throws()
    {
        using var harness = new BybitSocketTestHarness();
        var provider = CreateProvider(harness);
        await provider.StopAsync();

        var act = () => provider.StartAsync(new RecordingObserver(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StopAsync_Is_Idempotent_And_Stops_Delivery()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        await provider.StopAsync();
        await provider.StopAsync();

        harness.Push(PositionMessage(PositionItem()));
        harness.Push(WalletMessage());
        observer.Positions.Should().BeEmpty();
        observer.Wallets.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_Waits_For_Running_Callback_And_Rejects_Remaining_Observations()
    {
        using var harness = new BybitSocketTestHarness();
        var callback = new BlockingCallback();
        var observer = new RecordingObserver
        {
            OnPositionCallback = position =>
            {
                if (position.Symbol == "BTCUSDT")
                {
                    callback.Block();
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        var push = Task.Run(() => harness.Push(PositionMessage(PositionItem(), PositionItem(("symbol", "BTCPERP")))));
        await callback.Entered.WaitAsync(CompletionTimeout);
        var stop = provider.StopAsync();

        await AssertPendingAsync(stop);
        harness.Socket.IsOpen.Should().BeTrue();

        callback.Release();
        await stop.WaitAsync(CompletionTimeout);
        await push.WaitAsync(CompletionTimeout);

        observer.Positions.Should().ContainSingle().Which.Symbol.Should().Be("BTCUSDT");
        observer.TransportStates.Should().BeEmpty();
        await WaitUntilAsync(() => !harness.Socket.IsOpen);
    }

    [Fact]
    public async Task Position_Wallet_And_Transport_Events_After_Stop_Are_Not_Delivered()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);
        var positions = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var wallet = await CaptureWalletAsync(WalletMessage());

        await provider.StopAsync();
        await WaitUntilAsync(() => !harness.Socket.IsOpen);

        provider.HandlePositions(positions);
        provider.HandleWallet(wallet);
        provider.HandlePositions(UnmappablePositions());

        observer.Positions.Should().BeEmpty();
        observer.Wallets.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
        sink.Entries.Select(entry => entry.EventId.Id).Should().NotContain([1014, 1015, 1016]);
    }

    [Fact]
    public async Task Concurrent_StopAsync_Calls_All_Wait_For_Running_Callback()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var callback = new BlockingCallback();
        var observer = new RecordingObserver { OnPositionCallback = _ => callback.Block() };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        var push = Task.Run(() => harness.Push(PositionMessage(PositionItem())));
        await callback.Entered.WaitAsync(CompletionTimeout);
        var first = Task.Run(provider.StopAsync);
        var second = Task.Run(provider.StopAsync);

        await AssertPendingAsync(Task.WhenAny(first, second));

        callback.Release();
        await Task.WhenAll(first, second, push).WaitAsync(CompletionTimeout);

        await WaitUntilAsync(() => !harness.Socket.IsOpen);
        provider.StopAsync().IsCompletedSuccessfully.Should().BeTrue();
        observer.Positions.Should().ContainSingle();
        sink.Entries.Should().NotContain(entry => entry.EventId.Id == 1017);
    }

    [Fact]
    public async Task Observer_Failure_Releases_Callback_So_Stop_Completes()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var callback = new BlockingCallback();
        var calls = 0;
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    callback.Block();
                }

                throw new InvalidOperationException("observer failed BTCUSDT");
            },
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        harness.Push(PositionMessage(PositionItem()));
        var push = Task.Run(() => harness.Push(PositionMessage(PositionItem(("size", "0.7")))));
        await callback.Entered.WaitAsync(CompletionTimeout);
        var stop = provider.StopAsync();

        await AssertPendingAsync(stop);

        callback.Release();
        await stop.WaitAsync(CompletionTimeout);
        await push.WaitAsync(CompletionTimeout);

        observer.Positions.Should().HaveCount(2);
        sink.Entries.Where(entry => entry.EventId.Id == 1016).Should().HaveCount(2)
            .And.OnlyContain(entry => entry.LogLevel == LogLevel.Error);
        // Отказ во время остановки не порождает сигнал recovery от имени остановленного провайдера.
        observer.TransportStates.Should().ContainSingle()
            .Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        AssertNoSensitiveData(sink, "observer failed");
    }

    [Fact]
    public async Task Wallet_Message_Without_Applicable_Balances_Is_Delivered_As_Unconfirmed()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        harness.Push(PrivateMessage("wallet", CreationTimeMs));
        harness.Push(PrivateMessage("wallet", CreationTimeMs, """{"accountType":"FUND","totalEquity":"1"}"""));
        harness.Push(WalletMessage());
        harness.Push(PrivateMessage("wallet", CreationTimeMs, """{"totalEquity":"100"}"""));

        observer.Wallets.Select(wallet => wallet.Kind).Should().Equal(
            PrivateWalletObservationKind.Unconfirmed,
            PrivateWalletObservationKind.Unconfirmed,
            PrivateWalletObservationKind.PartialUpdate,
            PrivateWalletObservationKind.Unconfirmed);
        observer.Wallets.Select(wallet => wallet.RequiresRestReconciliation).Should().Equal(true, true, false, true);
        observer.Wallets[0].Balances.Should().BeEmpty();
        observer.Wallets[1].Balances.Should().BeEmpty();
        observer.Wallets[3].Balances.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
        await provider.StopAsync();
    }

    [Fact]
    public async Task Observer_Failure_Is_Logged_And_Does_Not_Stop_Transport()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ => throw new InvalidOperationException("observer failed BTCUSDT"),
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        harness.Push(PositionMessage(PositionItem()));
        harness.Push(PositionMessage(PositionItem(("size", "0.7"))));

        observer.Positions.Should().HaveCount(2);
        var entries = sink.Entries.Where(entry => entry.EventId.Id == 1016).ToArray();
        entries.Should().HaveCount(2).And.OnlyContain(entry => entry.LogLevel == LogLevel.Error);
        entries[0].Fields.Should().Contain(new KeyValuePair<string, object?>("ExceptionType", nameof(InvalidOperationException)));
        observer.TransportStates.Should().HaveCount(2).And.OnlyContain(state =>
            state.Status == RealtimeTransportStatus.MessageRejected && state.RecoveryRequired);
        harness.Socket.IsOpen.Should().BeTrue();
        AssertNoSensitiveData(sink, "observer failed");
        await provider.StopAsync();
    }

    [Fact]
    public async Task Rejected_Position_Observation_Is_Reported_As_Message_Rejected()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ => throw new InvalidOperationException("queue full BTCUSDT"),
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        var push = () => harness.Push(PositionMessage(PositionItem()));

        push.Should().NotThrow();
        observer.Positions.Should().ContainSingle();
        var state = observer.TransportStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        state.RecoveryRequired.Should().BeTrue();
        state.Failure.Should().BeNull();
        sink.Entries.Should().ContainSingle(entry => entry.EventId.Id == 1016)
            .Which.Fields.Should().Contain(new KeyValuePair<string, object?>("Stream", BybitRealtimeProvider.PositionStream));
        sink.Entries.Should().NotContain(entry => entry.EventId.Id == 1018);
        harness.Socket.IsOpen.Should().BeTrue();
        AssertNoSensitiveData(sink, "queue full");
        await provider.StopAsync();
    }

    [Fact]
    public async Task Repeat_Of_Rejected_Revision_Is_Not_Duplicate_Until_Delivered()
    {
        using var harness = new BybitSocketTestHarness();
        var rejectNext = true;
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ =>
            {
                if (rejectNext)
                {
                    rejectNext = false;
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        var message = PositionMessage(PositionItem());

        harness.Push(message);
        harness.Push(message);
        harness.Push(message);

        observer.Positions.Select(position => position.Kind).Should().Equal(
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Duplicate);
        observer.TransportStates.Should().ContainSingle()
            .Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Rejected_Position_In_Multi_Item_Message_Does_Not_Confirm_Its_Revision()
    {
        using var harness = new BybitSocketTestHarness();
        var rejectBtcUsdt = true;
        var observer = new RecordingObserver
        {
            OnPositionCallback = position =>
            {
                if (position.Symbol == "BTCUSDT" && rejectBtcUsdt)
                {
                    rejectBtcUsdt = false;
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        var message = PositionMessage(PositionItem(), PositionItem(("symbol", "BTCPERP")));

        harness.Push(message);
        harness.Push(message);

        observer.Positions.Select(position => (position.Symbol, position.Kind)).Should().Equal(
            ("BTCUSDT", PrivatePositionObservationKind.Open),
            ("BTCPERP", PrivatePositionObservationKind.Open),
            ("BTCUSDT", PrivatePositionObservationKind.Open),
            ("BTCPERP", PrivatePositionObservationKind.Duplicate));
        observer.TransportStates.Should().ContainSingle()
            .Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Undelivered_Recovery_Signal_Is_Repeated_Before_Next_Observation_Without_Recursion()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var positionAccepted = false;
        var transportAccepted = false;
        var observer = new RecordingObserver
        {
            OnTransportStateCallback = _ =>
            {
                if (!transportAccepted)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var transportStatesBeforePosition = new List<int>();
        observer.OnPositionCallback = _ =>
        {
            if (!positionAccepted)
            {
                throw new InvalidOperationException("queue full");
            }

            transportStatesBeforePosition.Add(observer.TransportStates.Count);
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);
        var message = PositionMessage(PositionItem());
        var push = () => harness.Push(message);

        push.Should().NotThrow();

        // Отказ доставки самого сигнала не порождает новый MessageRejected.
        observer.Positions.Should().ContainSingle();
        observer.TransportStates.Should().ContainSingle()
            .Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        sink.Entries.Where(entry => entry.EventId.Id == 1016).Should().HaveCount(2);
        sink.Entries.Should().ContainSingle(entry => entry.EventId.Id == 1018)
            .Which.LogLevel.Should().Be(LogLevel.Error);

        // Пока сигнал не принят, наблюдения не доставляются раньше него.
        positionAccepted = true;
        push.Should().NotThrow();
        harness.Push(WalletMessage());

        observer.Positions.Should().ContainSingle();
        observer.Wallets.Should().BeEmpty();
        observer.TransportStates.Should().HaveCount(3);

        // Канал принял сигнал: наблюдение доставлено после него и не стало Duplicate.
        transportAccepted = true;
        harness.Push(message);
        harness.Push(message);

        observer.Positions.Select(position => position.Kind).Should().Equal(
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Duplicate);
        transportStatesBeforePosition.Should().Equal(4, 4);
        observer.TransportStates.Should().HaveCount(4).And.OnlyContain(state =>
            state.Status == RealtimeTransportStatus.MessageRejected && state.RecoveryRequired);
        AssertNoSensitiveData(sink, "queue full");
        await provider.StopAsync();
    }

    [Fact]
    public async Task Delivered_Transport_State_Covers_Undelivered_Recovery_Signal()
    {
        using var harness = new BybitSocketTestHarness();
        var transportAccepted = false;
        var observer = new RecordingObserver
        {
            OnPositionCallback = position =>
            {
                if (position.Size == 0.5m)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
            OnTransportStateCallback = _ =>
            {
                if (!transportAccepted)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        harness.Push(PositionMessage(PositionItem()));
        observer.TransportStates.Should().ContainSingle();

        transportAccepted = true;
        provider.HandlePositions(UnmappablePositions());
        harness.Push(PositionMessage(PositionItem(("size", "0.7"), ("seq", Sequence + 1))));

        observer.TransportStates.Should().HaveCount(2, "принятое состояние уже сообщило о необходимости recovery")
            .And.OnlyContain(state => state.Status == RealtimeTransportStatus.MessageRejected);
        var positions = observer.Positions;
        positions.Should().HaveCount(2);
        positions[^1].Size.Should().Be(0.7m);
        positions[^1].Kind.Should().Be(PrivatePositionObservationKind.Open);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Accepted_Wallet_Observation_Is_Not_Reported_As_Message_Rejected()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        harness.Push(WalletMessage());

        var wallet = observer.Wallets.Should().ContainSingle().Subject;
        wallet.Kind.Should().Be(PrivateWalletObservationKind.PartialUpdate);
        wallet.RequiresRestReconciliation.Should().BeFalse();
        observer.TransportStates.Should().BeEmpty();
        sink.Entries.Should().NotContain(entry => entry.EventId.Id == 1016 || entry.EventId.Id == 1018);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Rejected_Wallet_Partial_Update_Is_Reported_As_Message_Rejected()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var walletAccepted = false;
        var observer = new RecordingObserver
        {
            OnWalletCallback = _ =>
            {
                if (!walletAccepted)
                {
                    throw new InvalidOperationException("queue full 12500");
                }
            },
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        var push = () => harness.Push(WalletMessage());

        push.Should().NotThrow();
        observer.Wallets.Should().ContainSingle().Which.Kind.Should().Be(PrivateWalletObservationKind.PartialUpdate);
        var state = observer.TransportStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        state.RecoveryRequired.Should().BeTrue();
        state.Failure.Should().BeNull();
        var entry = sink.Entries.Should().ContainSingle(log => log.EventId.Id == 1016).Subject;
        entry.LogLevel.Should().Be(LogLevel.Error);
        entry.Fields.Should().Contain(new KeyValuePair<string, object?>("Stream", BybitRealtimeProvider.WalletStream));
        sink.Entries.Should().NotContain(log => log.EventId.Id == 1018);
        harness.Socket.IsOpen.Should().BeTrue();

        walletAccepted = true;
        harness.Push(WalletMessage());

        observer.Wallets.Should().HaveCount(2);
        observer.TransportStates.Should().ContainSingle();
        AssertNoSensitiveData(sink, "queue full", "12500", "8000", "UNIFIED");
        await provider.StopAsync();
    }

    [Fact]
    public async Task Rejected_Unconfirmed_Wallet_Observation_Is_Reported_As_Message_Rejected()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver
        {
            OnWalletCallback = _ => throw new InvalidOperationException("queue full"),
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        harness.Push(PrivateMessage("wallet", CreationTimeMs, """{"accountType":"FUND","totalEquity":"1"}"""));

        var wallet = observer.Wallets.Should().ContainSingle().Subject;
        wallet.Kind.Should().Be(PrivateWalletObservationKind.Unconfirmed);
        wallet.RequiresRestReconciliation.Should().BeTrue();
        wallet.Balances.Should().BeEmpty();
        var state = observer.TransportStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        state.RecoveryRequired.Should().BeTrue();
        await provider.StopAsync();
    }

    [Fact]
    public async Task Undelivered_Recovery_Signal_After_Rejected_Wallet_Defers_Next_Observations_Without_Recursion()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var observer = new RecordingObserver
        {
            OnWalletCallback = _ => throw new InvalidOperationException("queue full"),
            OnTransportStateCallback = _ => throw new InvalidOperationException("queue full"),
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        var push = () => harness.Push(WalletMessage());

        push.Should().NotThrow();
        observer.Wallets.Should().ContainSingle();
        observer.TransportStates.Should().ContainSingle()
            .Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        sink.Entries.Where(entry => entry.EventId.Id == 1016).Should().HaveCount(2);
        sink.Entries.Should().ContainSingle(entry => entry.EventId.Id == 1018)
            .Which.Fields.Should().Contain(new KeyValuePair<string, object?>("Stream", BybitRealtimeProvider.WalletStream));

        // Каждое следующее наблюдение сначала повторяет непринятый сигнал и не доставляется без него.
        push.Should().NotThrow();
        harness.Push(PositionMessage(PositionItem()));

        observer.Wallets.Should().ContainSingle();
        observer.Positions.Should().BeEmpty();
        observer.TransportStates.Should().HaveCount(3).And.OnlyContain(state =>
            state.Status == RealtimeTransportStatus.MessageRejected && state.RecoveryRequired);
        sink.Entries.Where(entry => entry.EventId.Id == 1018).Should().HaveCount(3);
        harness.Socket.IsOpen.Should().BeTrue();
        AssertNoSensitiveData(sink, "queue full", "12500");
        await provider.StopAsync();
    }

    [Fact]
    public async Task Pending_Recovery_Signal_From_Rejected_Wallet_Is_Delivered_Before_Next_Wallet_Observation()
    {
        using var harness = new BybitSocketTestHarness();
        var walletAccepted = false;
        var transportAccepted = false;
        var observer = new RecordingObserver
        {
            OnTransportStateCallback = _ =>
            {
                if (!transportAccepted)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var transportStatesBeforeWallet = new List<int>();
        observer.OnWalletCallback = _ =>
        {
            if (!walletAccepted)
            {
                throw new InvalidOperationException("queue full");
            }

            transportStatesBeforeWallet.Add(observer.TransportStates.Count);
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        harness.Push(WalletMessage());
        observer.TransportStates.Should().ContainSingle();

        walletAccepted = true;
        transportAccepted = true;
        harness.Push(WalletMessage());

        transportStatesBeforeWallet.Should().Equal(2);
        observer.Wallets.Should().HaveCount(2);
        observer.TransportStates.Should().HaveCount(2).And.OnlyContain(state =>
            state.Status == RealtimeTransportStatus.MessageRejected && state.RecoveryRequired);

        // Доставленный сигнал покрыл потерю: следующее наблюдение его не повторяет.
        harness.Push(WalletMessage());

        transportStatesBeforeWallet.Should().Equal(2, 2);
        observer.TransportStates.Should().HaveCount(2);
        await provider.StopAsync();
    }

    [Fact]
    public async Task StopAsync_Waits_For_Rejecting_Wallet_Callback_Without_Recovery_Signal_After_Stop()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var callback = new BlockingCallback();
        var observer = new RecordingObserver
        {
            OnWalletCallback = _ =>
            {
                callback.Block();
                throw new InvalidOperationException("queue full");
            },
        };
        var wallet = await CaptureWalletAsync(WalletMessage());
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        var push = Task.Run(() => harness.Push(WalletMessage()));
        await callback.Entered.WaitAsync(CompletionTimeout);
        var stop = provider.StopAsync();

        await AssertPendingAsync(stop);
        harness.Socket.IsOpen.Should().BeTrue();

        callback.Release();
        await stop.WaitAsync(CompletionTimeout);
        await push.WaitAsync(CompletionTimeout);
        await WaitUntilAsync(() => !harness.Socket.IsOpen);
        provider.HandleWallet(wallet);

        observer.Wallets.Should().ContainSingle();
        // Отказ во время остановки не порождает сигнал recovery от имени остановленного провайдера.
        observer.TransportStates.Should().BeEmpty();
        sink.Entries.Should().ContainSingle(entry => entry.EventId.Id == 1016);
        sink.Entries.Should().NotContain(entry => entry.EventId.Id == 1018);
    }

    [Fact]
    public async Task Concurrent_Revisions_Of_Same_Position_Do_Not_Roll_Back_Confirmed_Revision()
    {
        using var harness = new BybitSocketTestHarness();
        var older = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var newer = await CapturePositionsAsync(PositionMessage(PositionItem(("size", "0.7"), ("seq", Sequence + 1))));
        var callback = new BlockingCallback();
        var blockOlder = 1;
        var observer = new RecordingObserver
        {
            OnPositionCallback = position =>
            {
                if (position.Size == 0.5m && Interlocked.Exchange(ref blockOlder, 0) == 1)
                {
                    callback.Block();
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        var olderDelivery = Task.Run(() => provider.HandlePositions(older));
        await callback.Entered.WaitAsync(CompletionTimeout);

        // Конкурентные ревизии ждут завершения доставки и классифицируются после её подтверждения.
        var concurrentDelivery = Task.Run(() =>
        {
            provider.HandlePositions(older);
            provider.HandlePositions(newer);
        });
        await AssertPendingAsync(concurrentDelivery);
        callback.Release();
        await Task.WhenAll(olderDelivery, concurrentDelivery).WaitAsync(CompletionTimeout);
        // Более старая ревизия после подтверждения новой не заменяет её.
        provider.HandlePositions(newer);
        provider.HandlePositions(older);

        observer.Positions.Select(position => (position.Size, position.Kind)).Should().Equal(
            (0.5m, PrivatePositionObservationKind.Open),
            (0.5m, PrivatePositionObservationKind.Duplicate),
            (0.7m, PrivatePositionObservationKind.Open),
            (0.7m, PrivatePositionObservationKind.Duplicate),
            (0.5m, PrivatePositionObservationKind.Open));
        observer.TransportStates.Should().BeEmpty();
        observer.MaxConcurrentCalls.Should().Be(1);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Recovery_Demand_Registered_During_Recovery_Signal_Delivery_Is_Not_Covered_By_It()
    {
        using var harness = new BybitSocketTestHarness();
        var first = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var second = await CapturePositionsAsync(PositionMessage(PositionItem(("size", "0.7"), ("seq", Sequence + 1))));
        var third = await CapturePositionsAsync(PositionMessage(PositionItem(("size", "0.8"), ("seq", Sequence + 2))));
        var repeatedSignal = new BlockingCallback();
        var positionCalls = 0;
        var transportCalls = 0;
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ =>
            {
                if (Interlocked.Increment(ref positionCalls) == 1)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
            OnTransportStateCallback = _ =>
            {
                switch (Interlocked.Increment(ref transportCalls))
                {
                    case 1:
                    case 3:
                        throw new InvalidOperationException("queue full");
                    case 2:
                        repeatedSignal.Block();
                        break;
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        // Первый demand: получатель отклонил и наблюдение, и сигнал о его потере.
        provider.HandlePositions(first);

        // Повтор сигнала удерживается получателем, пока конкурентно возникает новая потеря.
        var deliverSecond = Task.Run(() => provider.HandlePositions(second));
        await repeatedSignal.Entered.WaitAsync(CompletionTimeout);
        var reportLoss = Task.Run(() => provider.HandlePositions(UnmappablePositions()));
        var lossReportedDuringSignal = await Task.WhenAny(reportLoss, Task.Delay(PendingProbe)) == reportLoss;

        repeatedSignal.Release();
        await Task.WhenAll(deliverSecond, reportLoss).WaitAsync(CompletionTimeout);
        AssertNoObservationOvertakesPendingRecovery(observer.Calls);

        // Второй demand покрывается только принятым повтором сигнала перед следующим наблюдением.
        provider.HandlePositions(third);

        AssertNoObservationOvertakesPendingRecovery(observer.Calls);
        lossReportedDuringSignal.Should().BeFalse("регистрация recovery сериализована с доставкой сигнала");
        observer.Calls.Select(call => (call.Kind, call.Accepted)).Should().Equal(
            (ObserverCallKind.Position, false),
            (ObserverCallKind.TransportState, false),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.Position, true),
            (ObserverCallKind.TransportState, false),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.Position, true));
        observer.Positions.Select(position => (position.Size, position.Kind)).Should().Equal(
            (0.5m, PrivatePositionObservationKind.Open),
            (0.7m, PrivatePositionObservationKind.Open),
            (0.8m, PrivatePositionObservationKind.Open));
        observer.TransportStates.Should().HaveCount(4).And.OnlyContain(state =>
            state.Status == RealtimeTransportStatus.MessageRejected && state.RecoveryRequired);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Observation_Is_Not_Admitted_While_Recovery_Signal_Delivery_Is_Unresolved()
    {
        using var harness = new BybitSocketTestHarness();
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var signal = new BlockingCallback();
        var transportCalls = 0;
        var observer = new RecordingObserver
        {
            OnTransportStateCallback = _ =>
            {
                if (Interlocked.Increment(ref transportCalls) == 1)
                {
                    signal.Block();
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        // Сигнал о потере начат, но получатель ещё не принял и не отклонил его.
        var reportLoss = Task.Run(() => provider.HandlePositions(UnmappablePositions()));
        await signal.Entered.WaitAsync(CompletionTimeout);
        var deliverPosition = Task.Run(() => provider.HandlePositions(position));
        var admittedDuringSignal = await Task.WhenAny(deliverPosition, Task.Delay(PendingProbe)) == deliverPosition;

        // Отказ регистрирует demand раньше допуска наблюдения: оно ждёт принятого повтора.
        signal.Release();
        await Task.WhenAll(reportLoss, deliverPosition).WaitAsync(CompletionTimeout);

        observer.Calls.Select(call => (call.Kind, call.Accepted)).Should().Equal(
            (ObserverCallKind.TransportState, false),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.Position, true));
        AssertNoObservationOvertakesPendingRecovery(observer.Calls);
        admittedDuringSignal.Should().BeFalse();
        observer.MaxConcurrentCalls.Should().Be(1);
        observer.Positions.Should().ContainSingle().Which.Kind.Should().Be(PrivatePositionObservationKind.Open);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Concurrent_Position_And_Wallet_Callbacks_Wait_For_Recovery_Of_Rejected_Observation()
    {
        using var harness = new BybitSocketTestHarness();
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var wallet = await CaptureWalletAsync(WalletMessage());
        var walletCallback = new BlockingCallback();
        var walletCalls = 0;
        var observer = new RecordingObserver
        {
            OnWalletCallback = _ =>
            {
                if (Interlocked.Increment(ref walletCalls) == 1)
                {
                    walletCallback.Block();
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        var rejectedWallet = Task.Run(() => provider.HandleWallet(wallet));
        await walletCallback.Entered.WaitAsync(CompletionTimeout);
        var deliverPosition = Task.Run(() => provider.HandlePositions(position));
        var deliverWallet = Task.Run(() => provider.HandleWallet(wallet));
        await Task.WhenAny(deliverPosition, deliverWallet, Task.Delay(PendingProbe));
        var admittedDuringRejectedWallet = deliverPosition.IsCompleted || deliverWallet.IsCompleted;

        walletCallback.Release();
        await Task.WhenAll(rejectedWallet, deliverPosition, deliverWallet).WaitAsync(CompletionTimeout);

        var calls = observer.Calls;
        calls.Take(2).Select(call => (call.Kind, call.Accepted)).Should().Equal(
            (ObserverCallKind.Wallet, false),
            (ObserverCallKind.TransportState, true));
        calls.Skip(2).Select(call => (call.Kind, call.Accepted)).Should().BeEquivalentTo(
            [(ObserverCallKind.Position, true), (ObserverCallKind.Wallet, true)]);
        AssertNoObservationOvertakesPendingRecovery(calls);
        admittedDuringRejectedWallet.Should().BeFalse();
        observer.MaxConcurrentCalls.Should().Be(1);
        observer.TransportStates.Should().ContainSingle().Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Concurrent_Position_Wallet_And_Transport_Callbacks_Preserve_Recovery_Order()
    {
        using var harness = new BybitSocketTestHarness();
        DataEvent<BybitPositionUpdate[]>[] revisions =
        [
            await CapturePositionsAsync(PositionMessage(PositionItem())),
            await CapturePositionsAsync(PositionMessage(PositionItem(("size", "0.7"), ("seq", Sequence + 1)))),
            await CapturePositionsAsync(PositionMessage(PositionItem(("size", "0.9"), ("seq", Sequence + 2)))),
        ];
        var wallet = await CaptureWalletAsync(WalletMessage());
        var calls = 0;
        void RejectEveryFourthCall()
        {
            if (Interlocked.Increment(ref calls) % 4 == 0)
            {
                throw new InvalidOperationException("queue full");
            }
        }

        var observer = new RecordingObserver
        {
            OnPositionCallback = _ => RejectEveryFourthCall(),
            OnWalletCallback = _ => RejectEveryFourthCall(),
            OnTransportStateCallback = _ => RejectEveryFourthCall(),
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            await start.Task;
            for (var iteration = 0; iteration < 30; iteration++)
            {
                switch ((worker + iteration) % 3)
                {
                    case 0:
                        provider.HandlePositions(revisions[iteration % revisions.Length]);
                        break;
                    case 1:
                        provider.HandleWallet(wallet);
                        break;
                    default:
                        provider.HandlePositions(UnmappablePositions());
                        break;
                }
            }
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(CompletionTimeout);

        observer.MaxConcurrentCalls.Should().Be(1);
        AssertNoObservationOvertakesPendingRecovery(observer.Calls);
        observer.Calls.Should().Contain(call => call.Kind == ObserverCallKind.Position && call.Accepted)
            .And.Contain(call => call.Kind == ObserverCallKind.Wallet && call.Accepted)
            .And.Contain(call => !call.Accepted);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Repeatedly_Rejected_Recovery_Signal_Stays_Pending_Under_Concurrency_Without_Recursion()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var positionCalls = 0;
        var transportAccepted = 0;
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ =>
            {
                if (Interlocked.Increment(ref positionCalls) == 1)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
            OnTransportStateCallback = _ =>
            {
                if (Volatile.Read(ref transportAccepted) == 0)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);
        provider.HandlePositions(position);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                provider.HandlePositions(position);
            }
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(CompletionTimeout);

        // Каждая попытка повторяет сигнал ровно один раз; отказ сигнала не порождает новый сигнал.
        observer.Positions.Should().ContainSingle();
        observer.TransportStates.Should().HaveCount(21).And.OnlyContain(state =>
            state.Status == RealtimeTransportStatus.MessageRejected && state.RecoveryRequired);
        sink.Entries.Count(entry => entry.EventId.Id == 1018).Should().Be(21);
        observer.MaxConcurrentCalls.Should().Be(1);

        Volatile.Write(ref transportAccepted, 1);
        provider.HandlePositions(position);
        provider.HandlePositions(position);

        observer.Positions.Select(observation => observation.Kind).Should().Equal(
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Duplicate);
        observer.TransportStates.Should().HaveCount(22);
        AssertNoObservationOvertakesPendingRecovery(observer.Calls);
        AssertNoSensitiveData(sink, "queue full");
        await provider.StopAsync();
    }

    [Fact]
    public async Task Waiting_Repeat_Of_Rejected_Position_Revision_Is_Delivered_After_Recovery_And_Not_As_Duplicate()
    {
        using var harness = new BybitSocketTestHarness();
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var callback = new BlockingCallback();
        var positionCalls = 0;
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ =>
            {
                if (Interlocked.Increment(ref positionCalls) == 1)
                {
                    callback.Block();
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        var rejected = Task.Run(() => provider.HandlePositions(position));
        await callback.Entered.WaitAsync(CompletionTimeout);
        var repeat = Task.Run(() => provider.HandlePositions(position));
        var repeatDeliveredBeforeRejection = await Task.WhenAny(repeat, Task.Delay(PendingProbe)) == repeat;

        callback.Release();
        await Task.WhenAll(rejected, repeat).WaitAsync(CompletionTimeout);
        provider.HandlePositions(position);

        observer.Calls.Select(call => (call.Kind, call.Accepted)).Should().Equal(
            (ObserverCallKind.Position, false),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.Position, true),
            (ObserverCallKind.Position, true));
        observer.Positions.Select(observation => observation.Kind).Should().Equal(
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Duplicate);
        repeatDeliveredBeforeRejection.Should().BeFalse();
        AssertNoObservationOvertakesPendingRecovery(observer.Calls);
        await provider.StopAsync();
    }

    [Fact]
    public async Task StopAsync_During_Transport_Callback_Skips_Waiting_Position_And_Wallet_Deliveries()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var wallet = await CaptureWalletAsync(WalletMessage());
        var signal = new BlockingCallback();
        var observer = new RecordingObserver { OnTransportStateCallback = _ => signal.Block() };
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        var reportLoss = Task.Run(() => provider.HandlePositions(UnmappablePositions()));
        await signal.Entered.WaitAsync(CompletionTimeout);
        var deliverPosition = Task.Run(() => provider.HandlePositions(position));
        var deliverWallet = Task.Run(() => provider.HandleWallet(wallet));
        await Task.WhenAny(deliverPosition, deliverWallet, Task.Delay(PendingProbe));
        var first = Task.Run(provider.StopAsync);
        var second = Task.Run(provider.StopAsync);

        await AssertPendingAsync(Task.WhenAny(first, second));
        harness.Socket.IsOpen.Should().BeTrue();

        signal.Release();
        await Task.WhenAll(first, second, reportLoss, deliverPosition, deliverWallet).WaitAsync(CompletionTimeout);
        await WaitUntilAsync(() => !harness.Socket.IsOpen);
        provider.HandlePositions(position);
        provider.HandleWallet(wallet);
        provider.HandlePositions(UnmappablePositions());

        provider.StopAsync().IsCompletedSuccessfully.Should().BeTrue();
        observer.Positions.Should().BeEmpty();
        observer.Wallets.Should().BeEmpty();
        observer.Calls.Should().ContainSingle().Which.Kind.Should().Be(ObserverCallKind.TransportState);
        sink.Entries.Should().NotContain(entry => entry.EventId.Id == 1016 || entry.EventId.Id == 1017 || entry.EventId.Id == 1018);
    }

    [Fact]
    public async Task Blocked_Callback_Of_One_Account_Does_Not_Block_Provider_Of_Another_Account()
    {
        using var firstHarness = new BybitSocketTestHarness();
        using var secondHarness = new BybitSocketTestHarness();
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var wallet = await CaptureWalletAsync(WalletMessage());
        var callback = new BlockingCallback();
        var firstObserver = new RecordingObserver { OnPositionCallback = _ => callback.Block() };
        var secondObserver = new RecordingObserver();
        var firstProvider = CreateProvider(firstHarness);
        var secondProvider = CreateProvider(secondHarness);
        await firstProvider.StartAsync(firstObserver, CancellationToken.None);
        await secondProvider.StartAsync(secondObserver, CancellationToken.None);

        var blocked = Task.Run(() => firstProvider.HandlePositions(position));
        await callback.Entered.WaitAsync(CompletionTimeout);
        await Task.Run(() =>
        {
            secondProvider.HandlePositions(position);
            secondProvider.HandleWallet(wallet);
            secondProvider.HandlePositions(UnmappablePositions());
        }).WaitAsync(CompletionTimeout);

        blocked.IsCompleted.Should().BeFalse();
        secondObserver.Positions.Should().ContainSingle().Which.ExchangeAccountId.Should().Be(secondProvider.ExchangeAccountId);
        secondObserver.Wallets.Should().ContainSingle();
        secondObserver.TransportStates.Should().ContainSingle();
        firstObserver.Wallets.Should().BeEmpty();
        firstObserver.TransportStates.Should().BeEmpty();

        callback.Release();
        await blocked.WaitAsync(CompletionTimeout);
        await Task.WhenAll(firstProvider.StopAsync(), secondProvider.StopAsync()).WaitAsync(CompletionTimeout);
    }

    [Fact]
    public async Task Reentrant_Observation_From_Observer_Callback_Is_Not_Delivered_And_Requires_Recovery()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var wallet = await CaptureWalletAsync(WalletMessage());
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        var reentered = 0;
        var observer = new RecordingObserver();
        observer.OnPositionCallback = _ =>
        {
            if (Interlocked.Exchange(ref reentered, 1) == 0)
            {
                provider.HandleWallet(wallet);
            }
        };
        await provider.StartAsync(observer, CancellationToken.None);

        provider.HandlePositions(position);

        observer.Wallets.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
        sink.Entries.Should().ContainSingle(entry => entry.EventId.Id == 1019)
            .Which.Fields.Should().Contain(new KeyValuePair<string, object?>("Stream", BybitRealtimeProvider.WalletStream));

        provider.HandleWallet(wallet);

        observer.Calls.Select(call => (call.Kind, call.Accepted)).Should().Equal(
            (ObserverCallKind.Position, true),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.Wallet, true));
        observer.TransportStates.Should().ContainSingle().Which.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Recovery_Demand_Registered_Reentrantly_During_Recovery_Signal_Is_Not_Covered_By_It()
    {
        using var harness = new BybitSocketTestHarness();
        var position = await CapturePositionsAsync(PositionMessage(PositionItem()));
        var provider = CreateProvider(harness);
        var positionCalls = 0;
        var transportCalls = 0;
        var observer = new RecordingObserver
        {
            OnPositionCallback = _ =>
            {
                if (Interlocked.Increment(ref positionCalls) == 1)
                {
                    throw new InvalidOperationException("queue full");
                }
            },
        };
        observer.OnTransportStateCallback = _ =>
        {
            switch (Interlocked.Increment(ref transportCalls))
            {
                case 1:
                    throw new InvalidOperationException("queue full");
                case 2:
                    provider.HandlePositions(UnmappablePositions());
                    break;
            }
        };
        await provider.StartAsync(observer, CancellationToken.None);
        provider.HandlePositions(position);

        // Принятый повтор не покрывает demand, зарегистрированный во время его доставки.
        provider.HandlePositions(position);

        observer.Positions.Should().ContainSingle();
        provider.HandlePositions(position);

        observer.Calls.Select(call => (call.Kind, call.Accepted)).Should().Equal(
            (ObserverCallKind.Position, false),
            (ObserverCallKind.TransportState, false),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.TransportState, true),
            (ObserverCallKind.Position, true));
        observer.Positions[^1].Kind.Should().Be(PrivatePositionObservationKind.Open);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Mapper_Failure_Is_Reported_As_Message_Rejected()
    {
        using var harness = new BybitSocketTestHarness();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);
        await provider.StartAsync(observer, CancellationToken.None);

        provider.HandlePositions(UnmappablePositions());

        observer.Positions.Should().BeEmpty();
        var state = observer.TransportStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(RealtimeTransportStatus.MessageRejected);
        state.RecoveryRequired.Should().BeTrue();
        var entry = sink.Entries.Should().ContainSingle(log => log.EventId.Id == 1015).Subject;
        entry.Fields.Should().Contain(new KeyValuePair<string, object?>("Stream", BybitRealtimeProvider.PositionStream));
        AssertNoSensitiveData(sink);

        harness.Push(PositionMessage(PositionItem()));
        observer.Positions.Should().ContainSingle();
        await provider.StopAsync();
    }

    [Fact]
    public async Task Empty_Position_Array_Fails_Inside_Sdk_Before_Provider_Handler()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        // SDK 7.7.0 не может разобрать пустой data позиций до вызова обработчика, поэтому провайдер
        // не сообщает MessageRejected; в production исключение останавливает receive loop
        // CryptoExchange.Net, и о потере сообщения сигнализирует последующее переподключение.
        var push = () => harness.Push(PrivateMessage("position", CreationTimeMs));

        push.Should().Throw<InvalidOperationException>();
        observer.Positions.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
        await provider.StopAsync();
    }

    [Fact]
    public async Task Reconnect_Reports_Lost_And_Restored_Streams_As_Recovery_Required_And_Resumes_Delivery()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        harness.Push(PositionMessage(PositionItem()));

        await harness.Socket.ReconnectAsync();
        await WaitUntilAsync(() => observer.TransportStates.Count(state => state.Status == RealtimeTransportStatus.Restored) == 2);

        // Каждая из подписок position и wallet сообщает о потере и восстановлении своего потока.
        observer.TransportStates.Select(state => state.Status).Should().BeEquivalentTo(
        [
            RealtimeTransportStatus.ConnectionLost,
            RealtimeTransportStatus.ConnectionLost,
            RealtimeTransportStatus.Restored,
            RealtimeTransportStatus.Restored,
        ]);
        observer.TransportStates.Should().OnlyContain(state => state.RecoveryRequired && state.Failure == null);
        harness.SentMessages.Count(message => message.Contains("\"op\":\"auth\"")).Should().Be(2);
        harness.SentMessages.Count(message => message.Contains("\"subscribe\"") && message.Contains("\"position\"")).Should().Be(2);
        harness.SentMessages.Count(message => message.Contains("\"subscribe\"") && message.Contains("\"wallet\"")).Should().Be(2);

        harness.Push(PositionMessage(PositionItem(("size", "0.7"), ("seq", Sequence + 1))));
        harness.Push(WalletMessage());

        observer.Positions.Select(position => (position.Size, position.Kind)).Should().Equal(
            (0.5m, PrivatePositionObservationKind.Open),
            (0.7m, PrivatePositionObservationKind.Open));
        observer.Wallets.Should().ContainSingle();
        observer.TransportStates.Should().HaveCount(4);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Exact_Repeat_Of_Last_Position_Revision_Is_Marked_As_Duplicate()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        var original = PositionMessage(PositionItem());
        var changed = PositionMessage(PositionItem(("size", "0.7"), ("seq", Sequence + 1)));
        var closed = PositionMessage(PositionItem(("size", "0"), ("side", ""), ("seq", Sequence + 2)));

        harness.Push(original);
        harness.Push(original);
        harness.Push(changed);
        harness.Push(original);
        harness.Push(closed);
        harness.Push(closed);
        harness.Push(PositionMessage(PositionItem(("positionIdx", 1))));

        observer.Positions.Select(position => position.Kind).Should().Equal(
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Duplicate,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.PotentialClose,
            PrivatePositionObservationKind.Duplicate,
            PrivatePositionObservationKind.Open);
        observer.Positions[1].Position.Should().Be(observer.Positions[0].Position);
        observer.TransportStates.Should().BeEmpty();
        await provider.StopAsync();
    }

    [Fact]
    public async Task Ambiguous_Observations_Are_Not_Marked_As_Duplicate()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);
        var unknownSettlement = PositionMessage(PositionItem(("symbol", "NEWUSDT")));

        harness.Push(unknownSettlement);
        harness.Push(unknownSettlement);

        observer.Positions.Should().HaveCount(2).And.OnlyContain(position =>
            position.Issue == PrivatePositionObservationIssue.SettlementAssetUnconfirmed);
        await provider.StopAsync();
    }

    [Fact]
    public async Task Failure_Logs_Do_Not_Contain_Credentials_Provider_Messages_Or_Payload()
    {
        using var harness = new BybitSocketTestHarness(authSucceeds: false, apiKey: ApiKey, apiSecret: ApiSecret);
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var provider = CreateProvider(harness, loggerFactory: loggerFactory);

        await provider.StartAsync(new RecordingObserver(), CancellationToken.None);

        var entry = sink.Entries.Should().ContainSingle(log => log.EventId.Id == 1013).Subject;
        entry.LogLevel.Should().Be(LogLevel.Warning);
        entry.Fields.Should().Contain(new KeyValuePair<string, object?>("FailureKind", ExchangeFailureKind.InvalidCredentials));
        entry.Fields.Should().NotContain(field => field.Key == "Error" || field.Key == "Message");
        AssertNoSensitiveData(sink, "Invalid apikey", "Authentication failed");
    }

    [Fact]
    public void Constructor_Rejects_Uninitialized_Account()
    {
        using var harness = new BybitSocketTestHarness();

        var act = () => new BybitRealtimeProvider(
            harness.Client,
            InstrumentMetadata.Resolver(),
            default,
            LoggerFactory.Create(_ => { }).CreateLogger<BybitRealtimeProvider>(),
            TimeProvider.System);

        act.Should().Throw<ArgumentException>();
    }

    // Время получения на границе DateTime делает проверку правдоподобия отметок невычислимой.
    private static DataEvent<BybitPositionUpdate[]> UnmappablePositions() =>
        new(
            "Bybit",
            [new BybitPositionUpdate { Symbol = "BTCUSDT", UpdateTime = new DateTime(2023, 10, 19, 0, 0, 0, DateTimeKind.Utc) }],
            DateTime.MaxValue,
            "{}");

    /// <summary>
    /// Каждое наблюдение начинается только после принятого сигнала recovery, начатого позже
    /// последнего завершившегося до наблюдения отказа получателя.
    /// </summary>
    private static void AssertNoObservationOvertakesPendingRecovery(IReadOnlyList<ObserverCall> calls)
    {
        foreach (var observation in calls.Where(call => call.Kind != ObserverCallKind.TransportState))
        {
            var lastRejection = calls
                .Where(call => !call.Accepted && call.CompletedAt < observation.StartedAt)
                .MaxBy(call => call.CompletedAt);
            if (lastRejection is null)
            {
                continue;
            }

            calls.Should().Contain(
                call => call.Kind == ObserverCallKind.TransportState
                    && call.Accepted
                    && call.StartedAt > lastRejection.CompletedAt
                    && call.CompletedAt < observation.StartedAt,
                "наблюдение {0} не должно опережать непринятый сигнал recovery после отказа {1}",
                observation,
                lastRejection);
        }
    }

    private static async Task AssertPendingAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(PendingProbe));
        completed.Should().NotBeSameAs(task, "the task must wait for the running observer callback");
    }

    private static BybitRealtimeProvider CreateProvider(
        BybitSocketTestHarness harness,
        BybitInstrumentSettlementResolver? resolver = null,
        ILoggerFactory? loggerFactory = null) =>
        new(
            harness.Client,
            resolver ?? InstrumentMetadata.Resolver(("BTCUSDT", "USDT"), ("BTCPERP", "USDC")),
            ExchangeAccountId.New(),
            (loggerFactory ?? LoggerFactory.Create(_ => { })).CreateLogger<BybitRealtimeProvider>(),
            TimeProvider.System);

    private static void AssertNoSensitiveData(CapturingLoggerProvider sink, params string[] forbidden)
    {
        string[] always = [ApiKey, ApiSecret, "test-api-key-value", "test-api-secret-value", "BTCUSDT", "creationTime", "\"topic\""];
        foreach (var entry in sink.Entries)
        {
            foreach (var value in always.Concat(forbidden))
            {
                entry.Message.Should().NotContain(value);
                entry.Fields.Select(field => field.Value?.ToString() ?? string.Empty)
                    .Should().NotContain(fieldValue => fieldValue.Contains(value, StringComparison.Ordinal));
            }
        }
    }
}
