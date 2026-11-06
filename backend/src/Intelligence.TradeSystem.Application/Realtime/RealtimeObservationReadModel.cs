using Intelligence.TradeSystem.Application.Realtime.State;

namespace Intelligence.TradeSystem.Application.Realtime;

public enum RealtimeObservationReadStatus
{
    Unknown,
    Initializing,
    Connected,
    Reconnecting,
    Degraded,
    Stopped,
}

public sealed record RealtimeObservationReadModel(
    RealtimeObservationReadStatus Status,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastFullyReconciledAt,
    string? ReasonCode,
    bool HasState,
    bool RecoveryRequired,
    bool IsTransportOperational,
    RealtimeObservationTransportStatus? TransportStatus)
{
    public static RealtimeObservationReadModel Unknown { get; } = new(
        RealtimeObservationReadStatus.Unknown,
        null,
        null,
        null,
        null,
        false,
        false,
        false,
        null);
}
