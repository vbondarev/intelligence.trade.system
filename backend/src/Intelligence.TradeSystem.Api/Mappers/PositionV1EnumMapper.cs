using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;
using DomainPositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionV1EnumMapper
{
    public static PositionSideV1 ToWire(DomainPositionSide value) => value switch
    {
        DomainPositionSide.Long => PositionSideV1.Long,
        DomainPositionSide.Short => PositionSideV1.Short,
        _ => throw new NotSupportedException($"Position side '{value}' is not mapped to v1."),
    };

    public static PositionTrackingStateV1 ToWire(PositionTrackingState value) => value switch
    {
        PositionTrackingState.Active => PositionTrackingStateV1.Active,
        PositionTrackingState.Unknown => PositionTrackingStateV1.Unknown,
        PositionTrackingState.Stale => PositionTrackingStateV1.Stale,
        PositionTrackingState.Closed => PositionTrackingStateV1.Closed,
        _ => throw new NotSupportedException(
            $"Position tracking state '{value}' is not mapped to v1."),
    };

    public static MarketCategoryV1 ToWire(MarketCategory value) => value switch
    {
        MarketCategory.Linear => MarketCategoryV1.Linear,
        MarketCategory.Inverse => MarketCategoryV1.Inverse,
        _ => throw new NotSupportedException($"Market category '{value}' is not mapped to v1."),
    };

    public static AssessmentDataQualityV1 ToWire(AssessmentDataQuality value) => value switch
    {
        AssessmentDataQuality.FreshCompleteReliable => AssessmentDataQualityV1.FreshCompleteReliable,
        AssessmentDataQuality.Stale => AssessmentDataQualityV1.Stale,
        AssessmentDataQuality.Partial => AssessmentDataQualityV1.Partial,
        AssessmentDataQuality.Uncertain => AssessmentDataQualityV1.Uncertain,
        _ => throw new NotSupportedException(
            $"Assessment data quality '{value}' is not mapped to v1."),
    };

    public static AssessmentSafetyStateV1 ToWire(AssessmentSafetyState value) => value switch
    {
        AssessmentSafetyState.NotEvaluated => AssessmentSafetyStateV1.NotEvaluated,
        AssessmentSafetyState.Allowed => AssessmentSafetyStateV1.Allowed,
        AssessmentSafetyState.Blocked => AssessmentSafetyStateV1.Blocked,
        _ => throw new NotSupportedException(
            $"Assessment safety state '{value}' is not mapped to v1."),
    };

    public static RiskIncreaseDecisionV1 ToWire(RiskIncreaseDecision value) => value switch
    {
        RiskIncreaseDecision.Allowed => RiskIncreaseDecisionV1.Allowed,
        RiskIncreaseDecision.Blocked => RiskIncreaseDecisionV1.Blocked,
        _ => throw new NotSupportedException($"Risk decision '{value}' is not mapped to v1."),
    };

    public static PositionActionV1 ToWire(PositionAction value) => value switch
    {
        PositionAction.Hold => PositionActionV1.Hold,
        PositionAction.Watch => PositionActionV1.Watch,
        PositionAction.ProtectProfit => PositionActionV1.ProtectProfit,
        PositionAction.Reduce => PositionActionV1.Reduce,
        PositionAction.Close => PositionActionV1.Close,
        PositionAction.MoveStop => PositionActionV1.MoveStop,
        PositionAction.TakePartialProfit => PositionActionV1.TakePartialProfit,
        _ => throw new NotSupportedException($"Position action '{value}' is not mapped to v1."),
    };

    public static AddDecisionV1 ToWire(AddDecision value) => value switch
    {
        AddDecision.NotEvaluated => AddDecisionV1.NotEvaluated,
        AddDecision.DoNotAdd => AddDecisionV1.DoNotAdd,
        AddDecision.AddAllowed => AddDecisionV1.AddAllowed,
        _ => throw new NotSupportedException($"Add decision '{value}' is not mapped to v1."),
    };

    public static RecommendationPriorityV1 ToWire(RecommendationPriority value) => value switch
    {
        RecommendationPriority.Low => RecommendationPriorityV1.Low,
        RecommendationPriority.Normal => RecommendationPriorityV1.Normal,
        RecommendationPriority.High => RecommendationPriorityV1.High,
        RecommendationPriority.Critical => RecommendationPriorityV1.Critical,
        _ => throw new NotSupportedException(
            $"Recommendation priority '{value}' is not mapped to v1."),
    };

    public static RecommendationStatusV1 ToWire(RecommendationStatus value) => value switch
    {
        RecommendationStatus.Active => RecommendationStatusV1.Active,
        RecommendationStatus.Acknowledged => RecommendationStatusV1.Acknowledged,
        RecommendationStatus.Dismissed => RecommendationStatusV1.Dismissed,
        RecommendationStatus.Superseded => RecommendationStatusV1.Superseded,
        RecommendationStatus.Expired => RecommendationStatusV1.Expired,
        _ => throw new NotSupportedException(
            $"Recommendation status '{value}' is not mapped to v1."),
    };

    public static ReasonCodeV1 ToWire(ReasonCode value) => value switch
    {
        ReasonCode.PortfolioDataIncomplete => ReasonCodeV1.PortfolioDataIncomplete,
        ReasonCode.PortfolioDataStale => ReasonCodeV1.PortfolioDataStale,
        ReasonCode.InsufficientFreeCapital => ReasonCodeV1.InsufficientFreeCapital,
        ReasonCode.GrossExposureLimitExceeded => ReasonCodeV1.GrossExposureLimitExceeded,
        ReasonCode.ConcentrationLimitExceeded => ReasonCodeV1.ConcentrationLimitExceeded,
        ReasonCode.RiskWithinLimits => ReasonCodeV1.RiskWithinLimits,
        ReasonCode.MarketDataStale => ReasonCodeV1.MarketDataStale,
        ReasonCode.MarketDataPartial => ReasonCodeV1.MarketDataPartial,
        ReasonCode.MarketDataUncertain => ReasonCodeV1.MarketDataUncertain,
        ReasonCode.PortfolioDataUncertain => ReasonCodeV1.PortfolioDataUncertain,
        ReasonCode.RiskIncreaseBlockedByDataQuality => ReasonCodeV1.RiskIncreaseBlockedByDataQuality,
        ReasonCode.TrendAligned => ReasonCodeV1.TrendAligned,
        ReasonCode.TrendAdverse => ReasonCodeV1.TrendAdverse,
        ReasonCode.TrendFlatOrUnknown => ReasonCodeV1.TrendFlatOrUnknown,
        ReasonCode.MomentumNormal => ReasonCodeV1.MomentumNormal,
        ReasonCode.MomentumOverbought => ReasonCodeV1.MomentumOverbought,
        ReasonCode.MomentumOversold => ReasonCodeV1.MomentumOversold,
        ReasonCode.MomentumUnavailable => ReasonCodeV1.MomentumUnavailable,
        ReasonCode.MomentumExhaustion => ReasonCodeV1.MomentumExhaustion,
        ReasonCode.VolatilityUnavailable => ReasonCodeV1.VolatilityUnavailable,
        ReasonCode.SupportAvailable => ReasonCodeV1.SupportAvailable,
        ReasonCode.ResistanceAvailable => ReasonCodeV1.ResistanceAvailable,
        ReasonCode.SupportNearby => ReasonCodeV1.SupportNearby,
        ReasonCode.ResistanceNearby => ReasonCodeV1.ResistanceNearby,
        ReasonCode.LowVolume => ReasonCodeV1.LowVolume,
        ReasonCode.PnlPositive => ReasonCodeV1.PnlPositive,
        ReasonCode.PnlNegative => ReasonCodeV1.PnlNegative,
        ReasonCode.PnlUnavailable => ReasonCodeV1.PnlUnavailable,
        ReasonCode.PnlFlat => ReasonCodeV1.PnlFlat,
        ReasonCode.StopProtective => ReasonCodeV1.StopProtective,
        ReasonCode.StopNonProtective => ReasonCodeV1.StopNonProtective,
        ReasonCode.StopMissing => ReasonCodeV1.StopMissing,
        ReasonCode.StopUnknown => ReasonCodeV1.StopUnknown,
        ReasonCode.TrailingStopDistanceAvailable => ReasonCodeV1.TrailingStopDistanceAvailable,
        ReasonCode.BreakevenProfitable => ReasonCodeV1.BreakevenProfitable,
        ReasonCode.BreakevenUnprofitable => ReasonCodeV1.BreakevenUnprofitable,
        ReasonCode.BreakevenUnavailable => ReasonCodeV1.BreakevenUnavailable,
        ReasonCode.BreakevenAt => ReasonCodeV1.BreakevenAt,
        ReasonCode.LiquidationFar => ReasonCodeV1.LiquidationFar,
        ReasonCode.LiquidationNearby => ReasonCodeV1.LiquidationNearby,
        ReasonCode.LiquidationInvalid => ReasonCodeV1.LiquidationInvalid,
        ReasonCode.LiquidationUnavailable => ReasonCodeV1.LiquidationUnavailable,
        ReasonCode.RecommendationLimitedByDataQuality => ReasonCodeV1.RecommendationLimitedByDataQuality,
        ReasonCode.CloseConditionMet => ReasonCodeV1.CloseConditionMet,
        ReasonCode.LossReductionConditionMet => ReasonCodeV1.LossReductionConditionMet,
        ReasonCode.PartialProfitConditionMet => ReasonCodeV1.PartialProfitConditionMet,
        ReasonCode.ProfitProtectionNeeded => ReasonCodeV1.ProfitProtectionNeeded,
        ReasonCode.MoveStopConditionMet => ReasonCodeV1.MoveStopConditionMet,
        ReasonCode.StopNotProtectingProfit => ReasonCodeV1.StopNotProtectingProfit,
        ReasonCode.AddBlockedByAction => ReasonCodeV1.AddBlockedByAction,
        ReasonCode.AddBlockedByPortfolioRisk => ReasonCodeV1.AddBlockedByPortfolioRisk,
        ReasonCode.AddBlockedByLiquidation => ReasonCodeV1.AddBlockedByLiquidation,
        ReasonCode.AddBlockedByTrend => ReasonCodeV1.AddBlockedByTrend,
        ReasonCode.AddBlockedByMomentum => ReasonCodeV1.AddBlockedByMomentum,
        ReasonCode.AddBlockedByVolume => ReasonCodeV1.AddBlockedByVolume,
        ReasonCode.AddBlockedByStop => ReasonCodeV1.AddBlockedByStop,
        ReasonCode.AddMaximumSizeUnavailable => ReasonCodeV1.AddMaximumSizeUnavailable,
        ReasonCode.AddAllowedWithinLimits => ReasonCodeV1.AddAllowedWithinLimits,
        _ => throw new NotSupportedException($"Reason code '{value}' is not mapped to v1."),
    };
}
