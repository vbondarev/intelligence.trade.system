using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class MarketValuationMapper
{
    public static PositionMarketValuationResponse? ToResponse(MarketValuation? valuation) =>
        valuation is null
            ? null
            : new PositionMarketValuationResponse(
                valuation.MarkPrice,
                valuation.IndicativePositionValue,
                valuation.SettlementAsset?.Value,
                ToWireSource(valuation.Source),
                valuation.ObservedAt,
                valuation.CalculatedAt,
                ToWireStatus(valuation.FreshnessStatus));

    public static MarketValuationFreshnessStatusV1 ToWireStatus(
        MarketValuationFreshnessStatus status) => status switch
    {
        MarketValuationFreshnessStatus.Unknown => MarketValuationFreshnessStatusV1.Unknown,
        MarketValuationFreshnessStatus.Fresh => MarketValuationFreshnessStatusV1.Fresh,
        MarketValuationFreshnessStatus.Partial => MarketValuationFreshnessStatusV1.Partial,
        MarketValuationFreshnessStatus.Stale => MarketValuationFreshnessStatusV1.Stale,
        MarketValuationFreshnessStatus.NotApplicable => MarketValuationFreshnessStatusV1.NotApplicable,
        _ => throw new NotSupportedException(
            $"Market valuation status '{status}' is not mapped to a v1 wire contract."),
    };

    private static string? ToWireSource(MarketMarkPriceSource? source) =>
        source switch
        {
            null => null,
            MarketMarkPriceSource.PublicTicker => "publicTicker",
            _ => throw new NotSupportedException(
                $"Market mark price source '{source}' is not mapped to a v1 wire contract."),
        };
}
