namespace Intelligence.TradeSystem.Api.Realtime.V1;

public sealed record PositionUpdatedMessageV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid PositionId);
