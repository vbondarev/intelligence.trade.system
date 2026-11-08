using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class RealtimeObservationReadServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Missing_state_projects_unknown()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var store = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<RealtimeObservationState>?)null);
        var service = CreateService(store.Object);

        var result = await service.GetAsync(userId, accountId);

        result.Status.Should().Be(RealtimeObservationReadStatus.Unknown);
        result.HasState.Should().BeFalse();
        result.LastFullyReconciledAt.Should().BeNull();
        store.VerifyAll();
    }

    [Theory]
    [InlineData(RealtimeObservationTransportStatus.Initializing, RealtimeObservationReadStatus.Initializing)]
    [InlineData(RealtimeObservationTransportStatus.Reconnecting, RealtimeObservationReadStatus.Reconnecting)]
    [InlineData(RealtimeObservationTransportStatus.Degraded, RealtimeObservationReadStatus.Degraded)]
    [InlineData(RealtimeObservationTransportStatus.Stopped, RealtimeObservationReadStatus.Stopped)]
    public async Task Projects_saved_transport_status(
        RealtimeObservationTransportStatus transportStatus,
        RealtimeObservationReadStatus expectedStatus)
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var state = CreateState(accountId, transportStatus);
        var store = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<RealtimeObservationState>(state, ConcurrencyVersion.Initial));
        var service = CreateService(store.Object);

        var result = await service.GetAsync(userId, accountId);

        result.Status.Should().Be(expectedStatus);
        result.HasState.Should().BeTrue();
        result.TransportStatus.Should().Be(transportStatus);
        store.VerifyAll();
    }

    [Fact]
    public async Task Connected_state_with_stale_heartbeat_is_degraded()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var state = CreateState(accountId, RealtimeObservationTransportStatus.Connected) with
        {
            LastHeartbeatAt = Now - HeartbeatStaleAfter - TimeSpan.FromSeconds(1),
        };
        var store = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<RealtimeObservationState>(state, ConcurrencyVersion.Initial));
        var service = CreateService(store.Object);

        var result = await service.GetAsync(userId, accountId);

        result.Status.Should().Be(RealtimeObservationReadStatus.Degraded);
        result.ReasonCode.Should().Be("heartbeat_stale");
        result.IsTransportOperational.Should().BeFalse();
        store.VerifyAll();
    }

    [Fact]
    public async Task Connected_state_with_fresh_heartbeat_stays_connected_even_when_recovery_is_required()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var state = CreateState(accountId, RealtimeObservationTransportStatus.Connected) with
        {
            LastHeartbeatAt = Now,
            RecoveryRequired = true,
        };
        var store = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<RealtimeObservationState>(state, ConcurrencyVersion.Initial));
        var service = CreateService(store.Object);

        var result = await service.GetAsync(userId, accountId);

        result.Status.Should().Be(RealtimeObservationReadStatus.Connected);
        result.RecoveryRequired.Should().BeTrue();
        result.IsTransportOperational.Should().BeTrue();
        store.VerifyAll();
    }

    [Fact]
    public async Task Batch_read_projects_missing_entries_and_uses_one_store_call()
    {
        var userId = UserId.New();
        var knownAccountId = ExchangeAccountId.New();
        var missingAccountId = ExchangeAccountId.New();
        var state = CreateState(knownAccountId, RealtimeObservationTransportStatus.Connected) with
        {
            LastHeartbeatAt = Now,
        };
        var store = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        store.Setup(x => x.GetForAccountsAsync(
                userId,
                It.Is<IReadOnlyCollection<ExchangeAccountId>>(ids =>
                    ids.Count == 2 && ids.Contains(knownAccountId) && ids.Contains(missingAccountId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ExchangeAccountId, Versioned<RealtimeObservationState>>
            {
                [knownAccountId] = new(state, ConcurrencyVersion.Initial),
            });
        var service = CreateService(store.Object);

        var results = await service.GetForAccountsAsync(
            userId,
            [knownAccountId, missingAccountId, knownAccountId]);

        results[knownAccountId].Status.Should().Be(RealtimeObservationReadStatus.Connected);
        results[missingAccountId].Status.Should().Be(RealtimeObservationReadStatus.Unknown);
        store.Verify(
            x => x.GetForAccountsAsync(
                userId,
                It.IsAny<IReadOnlyCollection<ExchangeAccountId>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Empty_batch_does_not_read_persistence()
    {
        var store = new Mock<IRealtimeObservationStateStore>(MockBehavior.Strict);
        var service = CreateService(store.Object);

        var results = await service.GetForAccountsAsync(UserId.New(), []);

        results.Should().BeEmpty();
        store.VerifyNoOtherCalls();
    }

    private static RealtimeObservationReadService CreateService(IRealtimeObservationStateStore store) =>
        new(
            store,
            new RealtimeObservationReadOptions
            {
                HeartbeatStaleAfter = HeartbeatStaleAfter,
            },
            new FixedTimeProvider(Now));

    private static RealtimeObservationState CreateState(
        ExchangeAccountId accountId,
        RealtimeObservationTransportStatus status) =>
        new()
        {
            ExchangeAccountId = accountId,
            Generation = 1,
            TransportStatus = status,
            LastConnectedAt = Now.AddMinutes(-1),
            LastHeartbeatAt = status == RealtimeObservationTransportStatus.Connected
                ? Now.AddSeconds(-1)
                : null,
            LastFullyReconciledAt = Now.AddMinutes(-2),
            RecoveryRequired = false,
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
