namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Данные деривативного рынка.</summary>
public sealed record LlmDerivativesPayload
{
    public required decimal FundingRate { get; init; }
    public required decimal FundingRateAvg24h { get; init; }
    public DateTimeOffset? NextFundingTimeUtc { get; init; }
    public required decimal OpenInterest { get; init; }
    public required decimal OpenInterestValue { get; init; }
    public required decimal OpenInterestChange1hPct { get; init; }
    public required decimal OpenInterestChange4hPct { get; init; }
    public required decimal LongRatio { get; init; }
    public required decimal ShortRatio { get; init; }
    public decimal? PremiumVsIndexPct { get; init; }
}