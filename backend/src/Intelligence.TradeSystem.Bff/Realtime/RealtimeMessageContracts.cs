namespace Intelligence.TradeSystem.Bff.Realtime;

internal sealed record AccountUpdateMessage(Guid EventId, DateTimeOffset OccurredAt, Guid ExchangeAccountId);
internal sealed record PositionUpdateMessage(Guid EventId, DateTimeOffset OccurredAt, Guid PositionId);
