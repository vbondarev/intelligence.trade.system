using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime.State;

namespace Intelligence.TradeSystem.Application.Realtime;

public static class RealtimeFreshnessProjection
{
    public static RealtimeObservationReadModel ProjectObservation(
        RealtimeObservationState? state,
        DateTimeOffset now,
        TimeSpan heartbeatStaleAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(heartbeatStaleAfter, TimeSpan.Zero);

        if (state is null)
        {
            return new(
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

        var operational = state.IsTransportOperational(now, heartbeatStaleAfter);
        var status = state.TransportStatus switch
        {
            RealtimeObservationTransportStatus.Initializing => RealtimeObservationReadStatus.Initializing,
            RealtimeObservationTransportStatus.Connected when operational => RealtimeObservationReadStatus.Connected,
            RealtimeObservationTransportStatus.Connected => RealtimeObservationReadStatus.Degraded,
            RealtimeObservationTransportStatus.Reconnecting => RealtimeObservationReadStatus.Reconnecting,
            RealtimeObservationTransportStatus.Degraded => RealtimeObservationReadStatus.Degraded,
            RealtimeObservationTransportStatus.Stopped => RealtimeObservationReadStatus.Stopped,
            _ => throw new NotSupportedException(
                $"Transport status '{state.TransportStatus}' is not supported."),
        };
        var reasonCode = state.FailureCode;
        if (state.TransportStatus == RealtimeObservationTransportStatus.Connected && !operational)
        {
            reasonCode ??= "heartbeat_stale";
        }

        return new(
            status,
            state.LastConnectedAt,
            state.LastHeartbeatAt,
            state.LastFullyReconciledAt,
            reasonCode,
            true,
            state.RecoveryRequired,
            operational,
            state.TransportStatus);
    }

    public static FactualFreshnessReadModel ProjectFactualFreshness(
        PortfolioReadSummary summary,
        RealtimeObservationReadModel observation,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.RecoveryRequired
            || observation.Status is RealtimeObservationReadStatus.Degraded or RealtimeObservationReadStatus.Reconnecting
            || observation.TransportStatus == RealtimeObservationTransportStatus.Connected
                && !observation.IsTransportOperational)
        {
            return new(FactualFreshnessStatus.Degraded, observation.LastFullyReconciledAt);
        }

        if (!observation.HasState || observation.LastFullyReconciledAt is not { } lastFullyReconciledAt)
        {
            return new(FactualFreshnessStatus.Unknown, observation.LastFullyReconciledAt);
        }

        if (lastFullyReconciledAt > now || !summary.PositionsFullyReconciled)
        {
            return new(FactualFreshnessStatus.Degraded, lastFullyReconciledAt);
        }

        if (!summary.IsFresh || observation.TransportStatus == RealtimeObservationTransportStatus.Stopped)
        {
            return new(FactualFreshnessStatus.Stale, lastFullyReconciledAt);
        }

        return new(FactualFreshnessStatus.Fresh, lastFullyReconciledAt);
    }
}

public enum FactualFreshnessStatus
{
    Unknown,
    Fresh,
    Stale,
    Degraded,
}

public sealed record FactualFreshnessReadModel(
    FactualFreshnessStatus Status,
    DateTimeOffset? LastFullyReconciledAt);
