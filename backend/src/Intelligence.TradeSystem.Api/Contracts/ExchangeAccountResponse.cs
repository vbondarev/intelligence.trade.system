using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Contracts;

public sealed record ExchangeAccountResponse(
    Guid Id,
    ExchangeId Exchange,
    ExchangeAccountConnectionStatus ConnectionStatus,
    IReadOnlyList<ExchangeAccountCapabilities> Capabilities)
{
    public DateTimeOffset? LastSyncedAt { get; init; }
}
