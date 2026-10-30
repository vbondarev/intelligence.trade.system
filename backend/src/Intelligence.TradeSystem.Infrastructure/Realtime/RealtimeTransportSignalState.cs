using Intelligence.TradeSystem.Application.Realtime.Observations;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

internal sealed class RealtimeTransportSignalState
{
    private TransportSignal? _latestSignal;
    private long _persistedStateVersion;
    private long _persistedRecoveryRequestCount;
    private ConnectionState _connectionState = new(0, false);

    public bool IsConnected
    {
        get
        {
            var connectionState = Volatile.Read(ref _connectionState);
            if (!connectionState.Connected)
            {
                return false;
            }

            var latest = Volatile.Read(ref _latestSignal);
            if (latest is null)
            {
                return connectionState.Version == 0;
            }

            return connectionState.Version == latest.Version
                && latest.Version <= Interlocked.Read(ref _persistedStateVersion)
                && latest.RecoveryRequestCount <= Interlocked.Read(ref _persistedRecoveryRequestCount)
                && IsConnectedStatus(latest.State.Status);
        }
    }

    public TransportSignal? LatestSignal => Volatile.Read(ref _latestSignal);

    public TransportSignal Publish(RealtimeTransportState state, DateTimeOffset receivedAt)
    {
        while (true)
        {
            var current = Volatile.Read(ref _latestSignal);
            var next = new TransportSignal(
                state,
                checked((current?.Version ?? 0) + 1),
                checked((current?.RecoveryRequestCount ?? 0) + (state.RecoveryRequired ? 1 : 0)),
                receivedAt);
            if (ReferenceEquals(
                Interlocked.CompareExchange(ref _latestSignal, next, current),
                current))
            {
                return next;
            }
        }
    }

    public void MarkPending(TransportSignal signal) =>
        SetConnectedForVersion(signal.Version, connected: false);

    public bool IsSdkReconnectInProgress(DateTimeOffset now, TimeSpan staleAfter)
    {
        var latest = LatestSignal;
        if (latest is null || latest.State.Status != RealtimeTransportStatus.ConnectionLost)
        {
            return false;
        }

        var age = now - latest.ReceivedAt;
        return age >= TimeSpan.Zero && age < staleAfter;
    }

    public PendingTransportState? GetPending()
    {
        var latest = LatestSignal;
        if (latest is null)
        {
            return null;
        }

        var persistedVersion = Interlocked.Read(ref _persistedStateVersion);
        var persistedRecoveryRequestCount = Interlocked.Read(ref _persistedRecoveryRequestCount);
        var recoveryRequestCount = Math.Max(
            0,
            latest.RecoveryRequestCount - persistedRecoveryRequestCount);
        if (latest.Version <= persistedVersion && recoveryRequestCount == 0)
        {
            return null;
        }

        var state = latest.Version > persistedVersion
            ? latest.State
            : new RealtimeTransportState(RealtimeTransportStatus.MessageRejected, latest.ReceivedAt);
        return new PendingTransportState(
            state,
            recoveryRequestCount,
            latest.Version,
            latest.RecoveryRequestCount);
    }

    public void Acknowledge(PendingTransportState pending)
    {
        AdvanceMonotonic(ref _persistedStateVersion, pending.Version);
        AdvanceMonotonic(
            ref _persistedRecoveryRequestCount,
            pending.TotalRecoveryRequestCount);

        var latest = LatestSignal;
        if (latest?.Version == pending.Version
            && IsConnectedStatus(pending.State.Status))
        {
            SetConnectedForVersion(pending.Version, connected: true);
        }
    }

    public void MarkPersisted(RealtimeTransportState state)
    {
        if (!IsConnectedStatus(state.Status))
        {
            return;
        }

        var latest = LatestSignal;
        if (latest is null)
        {
            SetConnectedForVersion(0, connected: true);
            return;
        }

        if (latest.Version > Interlocked.Read(ref _persistedStateVersion)
            || latest.RecoveryRequestCount > Interlocked.Read(ref _persistedRecoveryRequestCount)
            || !IsConnectedStatus(latest.State.Status))
        {
            return;
        }

        SetConnectedForVersion(latest.Version, connected: true);
    }

    private void SetConnectedForVersion(long version, bool connected)
    {
        while (true)
        {
            var current = Volatile.Read(ref _connectionState);
            if (version < current.Version
                // Callback может дойти сюда после сохранения и подтверждения той же версии.
                || (version == current.Version && current.Connected && !connected)
                || (version == current.Version && current.Connected == connected))
            {
                return;
            }

            var next = new ConnectionState(version, connected);
            if (ReferenceEquals(
                Interlocked.CompareExchange(ref _connectionState, next, current),
                current))
            {
                return;
            }
        }
    }

    private static void AdvanceMonotonic(ref long location, long value)
    {
        var current = Interlocked.Read(ref location);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private static bool IsConnectedStatus(RealtimeTransportStatus status) =>
        status is RealtimeTransportStatus.Subscribed or RealtimeTransportStatus.Restored;

    internal sealed record TransportSignal(
        RealtimeTransportState State,
        long Version,
        long RecoveryRequestCount,
        DateTimeOffset ReceivedAt);

    internal sealed record PendingTransportState(
        RealtimeTransportState State,
        long RecoveryRequestCount,
        long Version,
        long TotalRecoveryRequestCount);

    private sealed record ConnectionState(long Version, bool Connected);
}
