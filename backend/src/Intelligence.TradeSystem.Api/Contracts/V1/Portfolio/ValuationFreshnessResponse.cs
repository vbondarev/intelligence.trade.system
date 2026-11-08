using Intelligence.TradeSystem.Api.Contracts.V1.Positions;

namespace Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;

public sealed record ValuationFreshnessResponse(
    MarketValuationFreshnessStatusV1 Status,
    DateTimeOffset? CalculatedAt);
