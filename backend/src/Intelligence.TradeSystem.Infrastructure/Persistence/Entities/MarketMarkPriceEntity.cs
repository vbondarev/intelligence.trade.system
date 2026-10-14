using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class MarketMarkPriceEntity
{
    public ExchangeId ExchangeId { get; set; }
    public MarketCategory MarketCategory { get; set; }
    public string InstrumentId { get; set; } = null!;
    public decimal MarkPrice { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public MarketMarkPriceSource Source { get; set; }
}
