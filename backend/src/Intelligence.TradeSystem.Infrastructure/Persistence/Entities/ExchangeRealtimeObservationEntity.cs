using Intelligence.TradeSystem.Application.Realtime.State;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class ExchangeRealtimeObservationEntity
{
    public Guid ExchangeAccountId { get; set; }
    public long Generation { get; set; }
    public RealtimeObservationTransportStatus TransportStatus { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastFullyReconciledAt { get; set; }
    public bool RecoveryRequired { get; set; }
    public long RecoveryRequestEpoch { get; set; }
    public string? FailureCode { get; set; }
    public long Version { get; set; }
}
