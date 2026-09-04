using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class ExchangeAccountEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ExchangeId ExchangeId { get; set; }
    public ExchangeAccountConnectionStatus ConnectionStatus { get; set; }
    public ExchangeAccountCapabilities Capabilities { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public long Version { get; set; }
}
