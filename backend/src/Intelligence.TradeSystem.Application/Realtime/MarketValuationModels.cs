using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

public enum MarketValuationFreshnessStatus
{
    Unknown = 0,
    Fresh = 1,
    Partial = 2,
    Stale = 3,
    NotApplicable = 4,
}

/// <summary>
/// Позиционная оценка, вычисленная из последней публичной mark price.
/// </summary>
/// <remarks>
/// <see cref="IndicativePositionValue"/> выражено в <see cref="SettlementAsset"/> и не заменяет
/// биржевое значение стоимости позиции.
/// </remarks>
public sealed record MarketValuation(
    decimal? MarkPrice,
    decimal? IndicativePositionValue,
    SettlementAsset? SettlementAsset,
    MarketMarkPriceSource? Source,
    DateTimeOffset? ObservedAt,
    DateTimeOffset CalculatedAt,
    MarketValuationFreshnessStatus FreshnessStatus);
