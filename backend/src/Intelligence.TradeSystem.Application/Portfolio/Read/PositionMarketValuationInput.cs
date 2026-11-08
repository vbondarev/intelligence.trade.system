using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public sealed record PositionMarketValuationInput(
    PositionId Id,
    ExchangeId ExchangeId,
    MarketCategory MarketCategory,
    string Symbol,
    SettlementAsset SettlementAsset,
    PositionTrackingState TrackingState,
    decimal Size);
