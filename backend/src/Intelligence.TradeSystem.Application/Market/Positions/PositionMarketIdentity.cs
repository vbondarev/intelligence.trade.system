using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Market.Positions;

public sealed record PositionMarketIdentity(
    PositionId PositionId,
    ExchangeAccountId ExchangeAccountId,
    ExchangeId ExchangeId,
    string Symbol,
    MarketCategory MarketCategory);
