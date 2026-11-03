namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

public sealed record RealtimeObservationResponse(
    RealtimeObservationStatusV1 Status,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastHeartbeatAt,
    string? ReasonCode);
