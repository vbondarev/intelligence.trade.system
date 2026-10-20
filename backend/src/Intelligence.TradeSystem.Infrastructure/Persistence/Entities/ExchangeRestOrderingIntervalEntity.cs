using Intelligence.TradeSystem.Application.Realtime.Ordering;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class ExchangeRestOrderingIntervalEntity
{
    public Guid Id { get; set; }
    public Guid ExchangeAccountId { get; set; }
    public ExchangeRestOrderingKind Kind { get; set; }
    public string? Symbol { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
