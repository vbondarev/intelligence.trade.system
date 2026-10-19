using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class RealtimeObservationStateModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Connected_state_with_heartbeat_within_threshold_confirms_operational_transport(int heartbeatOffsetSeconds)
    {
        var state = CreateState() with { LastHeartbeatAt = Now.AddSeconds(heartbeatOffsetSeconds) };

        state.IsTransportOperational(Now, StaleAfter).Should().BeTrue();
    }

    [Theory]
    [InlineData(RealtimeObservationTransportStatus.Initializing)]
    [InlineData(RealtimeObservationTransportStatus.Reconnecting)]
    [InlineData(RealtimeObservationTransportStatus.Degraded)]
    [InlineData(RealtimeObservationTransportStatus.Stopped)]
    public void Non_connected_status_does_not_confirm_transport(RealtimeObservationTransportStatus status)
    {
        var state = CreateState() with { TransportStatus = status, LastHeartbeatAt = Now };

        state.IsTransportOperational(Now, StaleAfter).Should().BeFalse();
    }

    [Fact]
    public void Connected_status_without_fresh_heartbeat_does_not_confirm_transport()
    {
        CreateState().IsTransportOperational(Now, StaleAfter).Should().BeFalse();
        (CreateState() with { LastHeartbeatAt = Now.AddSeconds(-61) })
            .IsTransportOperational(Now, StaleAfter)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3600)]
    public void Connected_status_with_heartbeat_ahead_of_now_does_not_confirm_transport(int heartbeatOffsetSeconds)
    {
        var state = CreateState() with { LastHeartbeatAt = Now.AddSeconds(heartbeatOffsetSeconds) };

        state.IsTransportOperational(Now, StaleAfter).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_heartbeat_threshold_is_rejected(int thresholdSeconds)
    {
        var act = () => (CreateState() with { LastHeartbeatAt = Now })
            .IsTransportOperational(Now, TimeSpan.FromSeconds(thresholdSeconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("auth.rejected")]
    [InlineData("Transport_Timeout-2")]
    public void Machine_readable_failure_code_is_accepted(string failureCode)
    {
        var state = CreateState() with { FailureCode = failureCode };

        state.FailureCode.Should().Be(failureCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1code")]
    [InlineData("invalid api key")]
    [InlineData("{\"retMsg\":\"x\"}")]
    [InlineData("код")]
    public void Free_text_failure_code_is_rejected(string failureCode)
    {
        var act = () => CreateState() with { FailureCode = failureCode };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Too_long_failure_code_is_rejected()
    {
        var act = () => CreateState() with
        {
            FailureCode = "a" + new string('b', RealtimeObservationState.FailureCodeMaxLength),
        };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_identity_generation_or_status_is_rejected()
    {
        ((Action)(() => _ = CreateState() with { ExchangeAccountId = default }))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = CreateState() with { Generation = 0 }))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = CreateState() with { TransportStatus = (RealtimeObservationTransportStatus)99 }))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Cursor_key_rejects_invalid_identity()
    {
        var account = ExchangeAccountId.New();
        var symbol = InstrumentId.From("BTCUSDT");

        ((Action)(() => PositionObservationCursorKey.Create(default, MarketCategory.Linear, symbol, 0)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => PositionObservationCursorKey.Create(account, (MarketCategory)99, symbol, 0)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => PositionObservationCursorKey.Create(account, MarketCategory.Linear, default, 0)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => PositionObservationCursorKey.Create(
                account,
                MarketCategory.Linear,
                InstrumentId.From(new string('A', PositionObservationCursorKey.SymbolMaxLength + 1)),
                0)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => PositionObservationCursorKey.Create(account, MarketCategory.Linear, symbol, -1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Cursor_key_identifies_position_without_side()
    {
        var account = ExchangeAccountId.New();
        var first = PositionObservationCursorKey.Create(account, MarketCategory.Linear, InstrumentId.From("BTCUSDT"), 0);
        var same = PositionObservationCursorKey.Create(account, MarketCategory.Linear, InstrumentId.From("BTCUSDT"), 0);
        var hedge = PositionObservationCursorKey.Create(account, MarketCategory.Linear, InstrumentId.From("BTCUSDT"), 1);

        first.Should().Be(same);
        first.Should().NotBe(hedge);
        first.ToString().Should().Be($"{account}/Linear/BTCUSDT/0");
    }

    [Fact]
    public void Cursor_requires_short_ascii_fingerprint_and_positive_sequence()
    {
        var key = PositionObservationCursorKey.Create(
            ExchangeAccountId.New(),
            MarketCategory.Linear,
            InstrumentId.From("BTCUSDT"),
            0);
        var cursor = new PositionObservationCursor
        {
            Key = key,
            LastProviderSequence = 1,
            LastAcceptedFingerprint = "sha256:AbC-1_2.3",
        };

        cursor.HasRevision.Should().BeTrue();
        (cursor with { LastProviderSequence = null }).HasRevision.Should().BeFalse();
        (cursor with { LastProviderSequence = null, LastProviderUpdatedAt = Now }).HasRevision.Should().BeTrue();
        ((Action)(() => _ = cursor with { LastProviderSequence = 0 }))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = cursor with { LastAcceptedFingerprint = "" }))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = cursor with { LastAcceptedFingerprint = "{\"size\":\"1\"}" }))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = cursor with
            {
                LastAcceptedFingerprint = new string('a', PositionObservationCursor.FingerprintMaxLength + 1),
            }))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = cursor with { Key = default }))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mark_price_rejects_invalid_public_market_values()
    {
        var price = new MarketMarkPrice
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = InstrumentId.From("BTCUSDT"),
            MarkPrice = 65000.5m,
            ObservedAt = Now,
            ReceivedAt = Now.AddMilliseconds(100),
            Source = MarketMarkPriceSource.PublicTicker,
        };

        price.MarkPrice.Should().Be(65000.5m);
        ((Action)(() => _ = price with { MarkPrice = 0m })).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = price with { Exchange = (ExchangeId)99 })).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = price with { Category = (MarketCategory)99 })).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = price with { Source = (MarketMarkPriceSource)99 })).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = price with { Symbol = default })).Should().Throw<ArgumentException>();
        ((Action)(() => _ = price with
            {
                Symbol = InstrumentId.From(new string('A', MarketMarkPrice.SymbolMaxLength + 1)),
            }))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Recovery_request_epoch_rejects_negative_values()
    {
        ((Action)(() => _ = CreateState() with { RecoveryRequestEpoch = -1 }))
            .Should().Throw<ArgumentOutOfRangeException>();
        (CreateState() with { RecoveryRequestEpoch = 0 }).RecoveryRequestEpoch.Should().Be(0);
        (CreateState() with { RecoveryRequestEpoch = long.MaxValue }).RecoveryRequestEpoch.Should().Be(long.MaxValue);
    }

    private static RealtimeObservationState CreateState() =>
        new()
        {
            ExchangeAccountId = ExchangeAccountId.New(),
            Generation = 1,
            TransportStatus = RealtimeObservationTransportStatus.Connected,
            RecoveryRequired = false,
        };
}
