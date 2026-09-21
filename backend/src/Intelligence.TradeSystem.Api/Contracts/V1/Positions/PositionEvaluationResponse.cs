namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions;

public sealed record PositionEvaluationResponse(
    Guid PositionId,
    PositionAssessmentResponse Assessment,
    PositionRecommendationResponse? Recommendation);

public sealed record PositionAssessmentResponse(
    Guid Id,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ValidUntil,
    string RuleVersion,
    bool IsLegacy,
    PositionEvaluationInputIdentityResponse InputIdentity,
    PolicyIdentityResponse BasePolicyIdentity,
    PolicyIdentityResponse EffectiveConfigurationIdentity,
    RiskIncreaseDecisionV1 PortfolioRiskDecision,
    IReadOnlyList<ReasonCodeV1> ReasonCodes,
    PositionAssessmentDataQualityResponse DataQuality,
    PositionAssessmentResultResponse? Result);

public sealed record PositionEvaluationInputIdentityResponse(
    Guid PositionId,
    Guid ExchangeAccountId,
    string InstrumentId,
    DateTimeOffset PositionObservedAt,
    DateTimeOffset PortfolioCalculatedAt,
    DateTimeOffset MarketCapturedAt);

public sealed record PolicyIdentityResponse(
    string Version,
    string Hash);

public sealed record PositionAssessmentDataQualityResponse(
    AssessmentDataQualityV1 Market,
    AssessmentDataQualityV1 Portfolio,
    AssessmentDataQualityV1 Overall,
    AssessmentSafetyStateV1 SafetyState);

public sealed record PositionAssessmentResultResponse(
    PositionSideV1 PositionSide,
    decimal? CurrentPrice,
    PositionAssessmentTrendResponse Trend,
    PositionAssessmentMomentumResponse Momentum,
    PositionAssessmentVolatilityResponse Volatility,
    PositionAssessmentLevelsResponse Levels,
    PositionAssessmentPnlResponse Pnl,
    PositionAssessmentStopResponse Stop,
    PositionAssessmentBreakevenResponse Breakeven,
    PositionAssessmentLiquidationResponse Liquidation,
    PositionAssessmentPortfolioRiskResponse PortfolioRisk);

public sealed record PositionAssessmentTrendResponse(
    AssessmentTrendDirectionV1 MarketTrend,
    PositionTrendAlignmentV1 PositionAlignment,
    decimal Strength,
    string Timeframe);

public sealed record PositionAssessmentMomentumResponse(
    decimal? Rsi14,
    bool IsReliable,
    AssessmentMomentumStateV1 State,
    bool PotentialExhaustion);

public sealed record PositionAssessmentVolatilityResponse(
    decimal? Atr14,
    decimal? AtrPercentOfPrice,
    bool IsReliable,
    bool IsFallback);

public sealed record PositionAssessmentLevelsResponse(
    decimal? CurrentPrice,
    decimal? Support1,
    decimal? DistanceToSupport1Percent,
    decimal? Support1Strength,
    decimal? Resistance1,
    decimal? DistanceToResistance1Percent,
    decimal? Resistance1Strength);

public sealed record PositionAssessmentPnlResponse(
    decimal? UnrealizedPnl,
    decimal? PnlPercent,
    decimal? AverageEntryPrice,
    decimal? CurrentPrice,
    AssessmentPricePositionV1 PriceRelativeToEntry,
    bool IsFavorable);

public sealed record PositionAssessmentStopResponse(
    decimal? StopPrice,
    decimal? DistanceFromCurrentPercent,
    decimal? StopRelativeToEntryPercent,
    AssessmentStopStateV1 State,
    AssessmentPricePositionV1 PriceRelativeToEntry,
    decimal? TrailingStopDistance,
    bool HasTrailingStop);

public sealed record PositionAssessmentBreakevenResponse(
    decimal? BreakEvenPrice,
    decimal? DistanceFromCurrentPercent,
    decimal? DistanceFromEntryPercent,
    AssessmentPricePositionV1 PriceRelativeToBreakEven,
    bool IsProfitable);

public sealed record PositionAssessmentLiquidationResponse(
    decimal? LiquidationPrice,
    decimal? DistanceFromCurrentPercent,
    AssessmentLiquidationStateV1 State);

public sealed record PositionAssessmentPortfolioRiskResponse(
    RiskIncreaseDecisionV1 PolicyDecision,
    decimal? FreeCapitalPercent,
    decimal? GrossExposureToEquityPercent,
    decimal? LargestPositionConcentrationPercent,
    decimal? TotalUnrealizedPnl,
    decimal? UsedCapital,
    bool IsComplete,
    bool IsFresh,
    decimal? TotalEquity,
    decimal? AvailableCapital,
    decimal? CurrentPositionValue,
    decimal? CurrentPositionConcentrationPercent,
    decimal? MinimumFreeCapitalPercent,
    decimal? MaximumGrossExposureToEquityPercent,
    decimal? MaximumPositionConcentrationPercent);

public sealed record PositionRecommendationResponse(
    Guid Id,
    Guid AssessmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidUntil,
    RecommendationStatusV1 Status,
    PolicyIdentityResponse PolicyIdentity,
    PositionRecommendationActionResponse Action,
    PositionRecommendationAddDecisionResponse AddDecision,
    IReadOnlyList<ReasonCodeV1> ReasonCodes,
    PositionRecommendationContinuationResponse? Continuation);

public sealed record PositionRecommendationActionResponse(
    PositionActionV1 Value,
    decimal? Confidence,
    RecommendationPriorityV1? Priority,
    IReadOnlyList<ReasonCodeV1> ReasonCodes);

public sealed record PositionRecommendationAddDecisionResponse(
    AddDecisionV1 Value,
    IReadOnlyList<ReasonCodeV1> ReasonCodes,
    decimal? MaximumAdditionalPositionValue,
    decimal? MaximumAdditionalQuantity,
    PositionRecommendationAddConditionsResponse? Conditions);

public sealed record PositionRecommendationAddConditionsResponse(
    PositionTrendAlignmentV1 RequiredTrendAlignment,
    AssessmentMomentumStateV1 RequiredMomentumState,
    bool ProtectiveStopRequired,
    decimal MinimumLiquidationDistancePercent);

public sealed record PositionRecommendationContinuationResponse(
    DateTimeOffset NextEvaluationAt,
    IReadOnlyList<PositionRecommendationConditionResponse> InvalidationConditions,
    IReadOnlyList<PositionRecommendationConditionResponse> ReevaluationConditions);

public sealed record PositionRecommendationConditionResponse(
    RecommendationConditionScopeV1 Scope,
    RecommendationConditionKindV1 Kind);

public enum RiskIncreaseDecisionV1
{
    Allowed,
    Blocked,
}

public enum AssessmentDataQualityV1
{
    FreshCompleteReliable,
    Stale,
    Partial,
    Uncertain,
}

public enum AssessmentSafetyStateV1
{
    NotEvaluated,
    Allowed,
    Blocked,
}

public enum AssessmentTrendDirectionV1
{
    Unknown,
    Bullish,
    Bearish,
    Sideways,
}

public enum PositionTrendAlignmentV1
{
    Aligned,
    Adverse,
    FlatOrUnknown,
}

public enum AssessmentMomentumStateV1
{
    Normal,
    Overbought,
    Oversold,
    Unavailable,
}

public enum AssessmentPricePositionV1
{
    Below,
    At,
    Above,
    Unavailable,
}

public enum AssessmentStopStateV1
{
    Protective,
    NonProtective,
    Unknown,
    Unavailable,
}

public enum AssessmentLiquidationStateV1
{
    Far,
    Near,
    Invalid,
    Unavailable,
}

public enum PositionActionV1
{
    Hold,
    Watch,
    ProtectProfit,
    Reduce,
    Close,
    MoveStop,
    TakePartialProfit,
}

public enum AddDecisionV1
{
    NotEvaluated,
    DoNotAdd,
    AddAllowed,
}

public enum RecommendationPriorityV1
{
    Low,
    Normal,
    High,
    Critical,
}

public enum RecommendationStatusV1
{
    Active,
    Acknowledged,
    Dismissed,
    Superseded,
    Expired,
}

public enum RecommendationConditionScopeV1
{
    Action,
    AddDecision,
    Recommendation,
}

public enum RecommendationConditionKindV1
{
    TrendAlignment,
    MomentumReliability,
    MomentumState,
    MomentumAvailability,
    MomentumExhaustion,
    StopState,
    StopAvailability,
    StopRelativePosition,
    ProfitProtection,
    LiquidationState,
    LiquidationDistance,
    LiquidationDistanceEligibility,
    PnlThreshold,
    PnlAvailability,
    HigherPriorityActions,
    AdditionalCapacityEligibility,
    DataQuality,
    SafetyState,
    PortfolioRiskDecision,
    LowVolume,
    PolicyIdentity,
    AddAllowedCapacity,
    OpposingLevel,
    RecommendationExpiry,
    ContinuationContextUnavailable,
}

public enum ReasonCodeV1
{
    PortfolioDataIncomplete,
    PortfolioDataStale,
    InsufficientFreeCapital,
    GrossExposureLimitExceeded,
    ConcentrationLimitExceeded,
    RiskWithinLimits,
    MarketDataStale,
    MarketDataPartial,
    MarketDataUncertain,
    PortfolioDataUncertain,
    RiskIncreaseBlockedByDataQuality,
    TrendAligned,
    TrendAdverse,
    TrendFlatOrUnknown,
    MomentumNormal,
    MomentumOverbought,
    MomentumOversold,
    MomentumUnavailable,
    MomentumExhaustion,
    VolatilityUnavailable,
    SupportAvailable,
    ResistanceAvailable,
    SupportNearby,
    ResistanceNearby,
    LowVolume,
    PnlPositive,
    PnlNegative,
    PnlUnavailable,
    PnlFlat,
    StopProtective,
    StopNonProtective,
    StopMissing,
    StopUnknown,
    TrailingStopDistanceAvailable,
    BreakevenProfitable,
    BreakevenUnprofitable,
    BreakevenUnavailable,
    BreakevenAt,
    LiquidationFar,
    LiquidationNearby,
    LiquidationInvalid,
    LiquidationUnavailable,
    RecommendationLimitedByDataQuality,
    CloseConditionMet,
    LossReductionConditionMet,
    PartialProfitConditionMet,
    ProfitProtectionNeeded,
    MoveStopConditionMet,
    StopNotProtectingProfit,
    AddBlockedByAction,
    AddBlockedByPortfolioRisk,
    AddBlockedByLiquidation,
    AddBlockedByTrend,
    AddBlockedByMomentum,
    AddBlockedByVolume,
    AddBlockedByStop,
    AddMaximumSizeUnavailable,
    AddAllowedWithinLimits,
}
