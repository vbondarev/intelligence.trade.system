namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class ExchangeWalletObservationCursorEntity
{
    public Guid ExchangeAccountId { get; set; }

    /// <summary>
    /// Последний принятый биржевой <c>MessageCreatedAt</c>; <see langword="null"/> не используется —
    /// отсутствие строки означает unknown.
    /// </summary>
    public DateTimeOffset LastMessageCreatedAt { get; set; }
}
