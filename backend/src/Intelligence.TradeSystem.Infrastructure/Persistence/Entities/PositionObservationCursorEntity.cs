using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class PositionObservationCursorEntity
{
    public Guid ExchangeAccountId { get; set; }
    public MarketCategory MarketCategory { get; set; }
    public string InstrumentId { get; set; } = null!;
    public int PositionIdx { get; set; }
    public long? LastProviderSequence { get; set; }
    public DateTimeOffset? LastProviderUpdatedAt { get; set; }
    public DateTimeOffset? LastMessageCreatedAt { get; set; }
    public string LastAcceptedFingerprint { get; set; } = null!;
    public long Version { get; set; }
}
