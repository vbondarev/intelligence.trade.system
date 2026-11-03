namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions;

public sealed record PositionMarketValuationResponse(
    decimal? MarkPrice,
    decimal? IndicativePositionValue,
    string? SettlementAsset,
    string? Source,
    DateTimeOffset? ObservedAt,
    DateTimeOffset CalculatedAt,
    MarketValuationFreshnessStatusV1 FreshnessStatus);
