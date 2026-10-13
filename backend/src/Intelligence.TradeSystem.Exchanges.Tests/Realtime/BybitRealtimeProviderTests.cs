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
        AssertNoSensitiveData(sink, "observer failed");
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

        // Время получения на границе DateTime делает проверку правдоподобия отметок невычислимой.
        provider.HandlePositions(new DataEvent<BybitPositionUpdate[]>(
            "Bybit",
            [new BybitPositionUpdate { Symbol = "BTCUSDT", UpdateTime = new DateTime(2023, 10, 19, 0, 0, 0, DateTimeKind.Utc) }],
            DateTime.MaxValue,
            "{}"));

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
    public async Task Empty_Position_Array_Fails_Inside_Sdk_And_Reconnect_Requires_Recovery()
    {
        using var harness = new BybitSocketTestHarness();
        var observer = new RecordingObserver();
        var provider = CreateProvider(harness);
        await provider.StartAsync(observer, CancellationToken.None);

        // SDK 7.7.0 не может разобрать пустой data позиций до вызова обработчика; в production
        // CryptoExchange.Net после этого переподключает соединение.
        var push = () => harness.Push(PrivateMessage("position", CreationTimeMs));

        push.Should().Throw<InvalidOperationException>();
        observer.Positions.Should().BeEmpty();

        await harness.Socket.ReconnectAsync();
        await WaitUntilAsync(() => observer.TransportStates.Any(state => state.Status == RealtimeTransportStatus.Restored));

        observer.TransportStates.Select(state => state.Status).Should().Contain(RealtimeTransportStatus.ConnectionLost);
        observer.TransportStates.Should().OnlyContain(state => state.RecoveryRequired);
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
