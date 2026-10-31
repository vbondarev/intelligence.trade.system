using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

public static class MarketValuationProjection
{
    public static readonly TimeSpan MarkPriceFreshnessAfter = TimeSpan.FromSeconds(5);

    public static MarketValuation Project(
        ExchangeId exchange,
        MarketCategory category,
        SettlementAsset settlementAsset,
        decimal size,
        PositionTrackingState trackingState,
        MarketMarkPrice? marketPrice,
        DateTimeOffset calculatedAt)
    {
        var supportedSettlement = IsSupportedSettlement(settlementAsset);
        var applicable = exchange == ExchangeId.Bybit
            && category == MarketCategory.Linear
            && trackingState != PositionTrackingState.Closed
            && supportedSettlement;
        SettlementAsset? settlement = string.IsNullOrWhiteSpace(settlementAsset.Value)
            ? null
            : settlementAsset;

        if (!applicable)
        {
            return new(
                null,
                null,
                settlement,
                null,
                null,
                calculatedAt,
                MarketValuationFreshnessStatus.NotApplicable);
        }

        if (marketPrice is null)
        {
            return new(
                null,
                null,
                settlement,
                null,
                null,
                calculatedAt,
                MarketValuationFreshnessStatus.Unknown);
        }

        var age = calculatedAt - marketPrice.ObservedAt;
        var isFresh = age >= TimeSpan.Zero && age < MarkPriceFreshnessAfter;
        decimal? indicativePositionValue = null;
        if (size > 0m)
        {
            try
            {
                // В Bybit Linear размер позиции выражен в базовом активе, поэтому произведение даёт номинал в settle asset.
                indicativePositionValue = size * marketPrice.MarkPrice;
            }
            catch (OverflowException)
            {
                // Не публикуем производное значение, которое невозможно представить как decimal.
            }
        }

        var freshnessStatus = !isFresh
            ? MarketValuationFreshnessStatus.Stale
            : indicativePositionValue is null
                ? MarketValuationFreshnessStatus.Partial
                : MarketValuationFreshnessStatus.Fresh;

        return new(
            marketPrice.MarkPrice,
            indicativePositionValue,
            settlement,
            marketPrice.Source,
            marketPrice.ObservedAt,
            calculatedAt,
            freshnessStatus);
    }

    private static bool IsSupportedSettlement(SettlementAsset settlementAsset) =>
        string.Equals(settlementAsset.Value, "USDT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(settlementAsset.Value, "USDC", StringComparison.OrdinalIgnoreCase);
}
