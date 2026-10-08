using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Api.Serialization;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionMapper
{
    public static CursorPage<PositionListItemResponse> ToResponse(PositionReadPage page) =>
        new(
            page.Items.Select(ToResponse).ToArray(),
            page.NextCursor is { } nextCursor
                ? PositionCursorCodec.Encode(nextCursor)
                : null,
            page.HasMore);

    public static PositionListItemResponse ToResponse(PositionReadListItem item) => new(
        item.Id.Value,
        item.ExchangeAccountId.Value,
        item.Symbol,
        PositionV1EnumMapper.ToWire(item.Side),
        PositionV1EnumMapper.ToWire(item.TrackingState),
        item.Size,
        item.AverageEntryPrice,
        item.MarkPrice,
        item.PositionValue,
        item.UnrealizedPnl,
        item.Leverage,
        item.LiquidationPrice,
        item.FirstDetectedAt,
        item.LastObservedAt,
        item.ClosedAt,
        item.SettlementAsset.Value);

    public static PositionResponse ToResponse(PositionReadDetail detail)
    {
        var item = detail.ListItem;
        return new PositionResponse(
            item.Id.Value,
            item.ExchangeAccountId.Value,
            item.Symbol,
            PositionV1EnumMapper.ToWire(item.Side),
            PositionV1EnumMapper.ToWire(item.TrackingState),
            item.Size,
            item.AverageEntryPrice,
            item.MarkPrice,
            item.PositionValue,
            item.UnrealizedPnl,
            item.Leverage,
            item.LiquidationPrice,
            item.FirstDetectedAt,
            item.LastObservedAt,
            item.ClosedAt,
            PositionV1EnumMapper.ToWire(detail.MarketCategory),
            detail.BreakEvenPrice,
            detail.TakeProfit,
            detail.StopLoss,
            detail.TrailingStop,
            item.SettlementAsset.Value);
    }
}
