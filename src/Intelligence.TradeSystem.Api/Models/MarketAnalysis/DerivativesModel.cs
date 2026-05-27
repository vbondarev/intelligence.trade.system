namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO деривативных метрик инструмента.</summary>
public sealed record DerivativesModel
{
    public decimal FundingRate { get; init; }
    public DateTimeOffset? NextFundingTimeUtc { get; init; }
    public decimal OpenInterest { get; init; }
    public decimal OpenInterestValue { get; init; }
    public decimal LongRatio { get; init; }
    public decimal ShortRatio { get; init; }
    public decimal? PremiumVsIndexPct { get; init; }
    public decimal OpenInterestChange1hPct { get; init; }
    public decimal OpenInterestChange4hPct { get; init; }
    public decimal FundingRateAvg24h { get; init; }
}
