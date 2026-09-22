namespace Intelligence.TradeSystem.Api.Realtime.V1;

public sealed record ExchangeAccountUpdatedMessageV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ExchangeAccountId);
