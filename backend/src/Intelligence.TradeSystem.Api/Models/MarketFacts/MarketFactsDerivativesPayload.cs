namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Данные деривативного рынка.
/// </summary>
public sealed record MarketFactsDerivativesPayload
{
    /// <summary>Текущая ставка финансирования.</summary>
    public decimal? FundingRate { get; init; }

    /// <summary>Средняя ставка финансирования за 24 часа.</summary>
    public decimal? FundingRateAvg24h { get; init; }

    /// <summary>Время следующего финансирования (UTC).</summary>
    public DateTimeOffset? NextFundingTimeUtc { get; init; }

    /// <summary>Открытый интерес в базовой валюте.</summary>
    public decimal? OpenInterest { get; init; }

    /// <summary>Открытый интерес в USD.</summary>
    public decimal? OpenInterestValue { get; init; }

    /// <summary>Изменение открытого интереса за 1 час в процентах.</summary>
    public decimal? OpenInterestChange1hPct { get; init; }

    /// <summary>Изменение открытого интереса за 4 часа в процентах.</summary>
    public decimal? OpenInterestChange4hPct { get; init; }

    /// <summary>Доля длинных позиций (long ratio).</summary>
    public decimal? LongRatio { get; init; }

    /// <summary>Доля коротких позиций (short ratio).</summary>
    public decimal? ShortRatio { get; init; }

    /// <summary>Премия mark price над index price в процентах.</summary>
    public decimal? PremiumVsIndexPct { get; init; }
}
