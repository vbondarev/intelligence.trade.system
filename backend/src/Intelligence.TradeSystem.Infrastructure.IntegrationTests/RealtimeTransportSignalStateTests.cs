using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Infrastructure.Realtime;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class RealtimeTransportSignalStateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Delayed_restored_callback_cannot_clear_a_persisted_acknowledgement()
    {
        var state = new RealtimeTransportSignalState();
        state.MarkPersisted(new RealtimeTransportState(RealtimeTransportStatus.Subscribed, Now));
        Assert.True(state.IsConnected);

        var callback = state.Publish(
            new RealtimeTransportState(RealtimeTransportStatus.Restored, Now.AddSeconds(1)),
            Now.AddSeconds(1));
        var pending = Assert.IsType<RealtimeTransportSignalState.PendingTransportState>(state.GetPending());

        Assert.False(state.IsConnected);
        Assert.Equal(RealtimeTransportStatus.Restored, pending.State.Status);
        state.Acknowledge(pending);
        Assert.True(state.IsConnected);

        state.MarkPending(callback);

        Assert.True(state.IsConnected);
        Assert.Null(state.GetPending());
    }

    [Fact]
    public void Initial_persistence_cannot_confirm_a_later_unacknowledged_restored_signal()
    {
        var state = new RealtimeTransportSignalState();
        state.MarkPersisted(new RealtimeTransportState(RealtimeTransportStatus.Subscribed, Now));
        Assert.True(state.IsConnected);

        var callback = state.Publish(
            new RealtimeTransportState(RealtimeTransportStatus.Restored, Now.AddSeconds(1)),
            Now.AddSeconds(1));
        state.MarkPersisted(new RealtimeTransportState(RealtimeTransportStatus.Subscribed, Now));

        Assert.False(state.IsConnected);
        Assert.Equal(
            RealtimeTransportStatus.Restored,
            Assert.IsType<RealtimeTransportSignalState.PendingTransportState>(state.GetPending())
                .State.Status);

        state.MarkPending(callback);

        Assert.False(state.IsConnected);
    }

    [Fact]
    public void Later_connection_loss_fences_an_acknowledgement_of_an_older_restored_signal()
    {
        var state = new RealtimeTransportSignalState();
        var restored = state.Publish(
            new RealtimeTransportState(RealtimeTransportStatus.Restored, Now),
            Now);
        state.MarkPending(restored);
        var restoredPending = Assert.IsType<RealtimeTransportSignalState.PendingTransportState>(
            state.GetPending());
        state.Acknowledge(restoredPending);
        Assert.True(state.IsConnected);

        var lost = state.Publish(
            new RealtimeTransportState(RealtimeTransportStatus.ConnectionLost, Now.AddSeconds(1)),
            Now.AddSeconds(1));
        state.MarkPending(lost);
        state.Acknowledge(restoredPending);

        Assert.False(state.IsConnected);
        var pendingLoss = Assert.IsType<RealtimeTransportSignalState.PendingTransportState>(
            state.GetPending());
        Assert.Equal(RealtimeTransportStatus.ConnectionLost, pendingLoss.State.Status);
        Assert.True(pendingLoss.State.RecoveryRequired);
        Assert.Equal(1, pendingLoss.RecoveryRequestCount);
        Assert.Equal(2, pendingLoss.Version);
    }

    [Theory]
    [InlineData(
        RealtimeTransportStatus.ConnectionLost,
        RealtimeTransportStatus.Restored,
        null)]
    [InlineData(
        RealtimeTransportStatus.Restored,
        RealtimeTransportStatus.ConnectionLost,
        null)]
    [InlineData(
        RealtimeTransportStatus.ConnectionLost,
        RealtimeTransportStatus.Restored,
        RealtimeTransportStatus.ConnectionLost)]
    [InlineData(
        RealtimeTransportStatus.Restored,
        RealtimeTransportStatus.Restored,
        RealtimeTransportStatus.Restored)]
    public void Concurrent_signal_publication_preserves_latest_version_and_recovery_count(
        RealtimeTransportStatus first,
        RealtimeTransportStatus second,
        RealtimeTransportStatus? third)
    {
        var state = new RealtimeTransportSignalState();
        var statuses = new List<RealtimeTransportStatus> { first, second };
        if (third is { } thirdStatus)
        {
            statuses.Add(thirdStatus);
        }

        var signals = statuses
            .Select((status, index) =>
            {
                var occurredAt = Now.AddSeconds(index);
                return state.Publish(new RealtimeTransportState(status, occurredAt), occurredAt);
            })
            .ToArray();

        foreach (var signal in signals.Reverse())
        {
            state.MarkPending(signal);
        }

        var pending = Assert.IsType<RealtimeTransportSignalState.PendingTransportState>(
            state.GetPending());
        var expectedRecoveryCount = signals.Count(signal => signal.State.RecoveryRequired);
        Assert.Equal((long)signals.Length, pending.Version);
        Assert.Equal((long)expectedRecoveryCount, pending.RecoveryRequestCount);
        Assert.Equal(statuses[^1], pending.State.Status);

        state.Acknowledge(pending);

        Assert.Equal(
            statuses[^1] is RealtimeTransportStatus.Restored or RealtimeTransportStatus.Subscribed,
            state.IsConnected);
        Assert.Null(state.GetPending());
    }
}
