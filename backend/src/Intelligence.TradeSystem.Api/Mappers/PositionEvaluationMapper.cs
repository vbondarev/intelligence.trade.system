using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;
using DomainAddDecision = Intelligence.TradeSystem.Domain.Decisions.AddDecision;
using DomainPositionAction = Intelligence.TradeSystem.Domain.Decisions.PositionAction;
using DomainRecommendationPriority = Intelligence.TradeSystem.Domain.Recommendations.RecommendationPriority;
using DomainRecommendationStatus = Intelligence.TradeSystem.Domain.Recommendations.RecommendationStatus;
using DomainRiskIncreaseDecision = Intelligence.TradeSystem.Domain.Decisions.RiskIncreaseDecision;
using DomainPositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionEvaluationMapper
{
    public static PositionEvaluationResponse ToResponse(PositionEvaluationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Assessment.PositionId.Value,
            ToResponse(snapshot.Assessment),
            snapshot.Recommendation is null
                ? null
                : ToResponse(snapshot.Recommendation));
    }

    private static PositionAssessmentResponse ToResponse(
        PositionAssessment assessment)
    {
        var input = assessment.InputVersions;
        return new(
            assessment.Id.Value,
            assessment.CreatedAt,
            assessment.ValidUntil,
            assessment.RuleVersion.Value,
            assessment.Result.IsLegacy,
            new(
                input.PositionId.Value,
                input.ExchangeAccountId.Value,
                input.InstrumentId.Value!,
                input.PositionObservedAt,
                input.PortfolioCalculatedAt,
                input.MarketCapturedAt),
            ToResponse(input.BasePolicyConfigurationIdentity),
            ToResponse(input.PolicyConfigurationIdentity),
            ToWire(assessment.PortfolioRiskDecision),
            assessment.ReasonCodes.Select(ToWire).ToArray(),
            ToResponse(assessment.Result.DataQuality),
            assessment.Result.IsLegacy ? null : ToResponse(assessment.Result));
    }

    private static PositionRecommendationResponse ToResponse(Recommendation recommendation) =>
        new(
            recommendation.Id.Value,
            recommendation.AssessmentId.Value,
            recommendation.CreatedAt,
            recommendation.ValidUntil,
            ToWire(recommendation.Status),
            ToResponse(recommendation.PolicyIdentity),
            new(
                ToWire(recommendation.ActionDecision.Action),
                recommendation.Confidence,
                recommendation.Priority is { } priority ? ToWire(priority) : null,
                recommendation.ActionReasonCodes.Select(ToWire).ToArray()),
            new(
                ToWire(recommendation.AddDecisionResult.Decision),
                recommendation.AddReasonCodes.Select(ToWire).ToArray(),
                recommendation.MaximumAdditionalPositionValue,
                recommendation.MaximumAdditionalQuantity,
                recommendation.AddConditions is { } conditions
                    ? new(
                        ToWire(conditions.RequiredTrendAlignment),
                        ToWire(conditions.RequiredMomentumState),
                        conditions.ProtectiveStopRequired,
                        conditions.MinimumLiquidationDistancePercent)
                    : null),
            recommendation.ReasonCodes.Select(ToWire).ToArray(),
            recommendation.ContinuationPlan is { } continuation
                ? new(
                    continuation.NextEvaluationAt,
                    continuation.InvalidationConditions
                        .Select(ToResponse)
                        .ToArray(),
                    continuation.ReevaluationConditions
                        .Select(ToResponse)
                        .ToArray())
                : null);

    private static PositionAssessmentResultResponse ToResponse(
        PositionAssessmentResult result) =>
        new(
            ToWire(result.PositionSide),
            result.CurrentPrice,
            new(
                ToWire(result.Trend.MarketTrend),
                ToWire(result.Trend.PositionAlignment),
                result.Trend.Strength,
                result.Trend.Timeframe),
            new(
                result.Momentum.Rsi14,
                result.Momentum.IsReliable,
                ToWire(result.Momentum.State),
                result.Momentum.PotentialExhaustion),
            new(
                result.Volatility.Atr14,
                result.Volatility.AtrPercentOfPrice,
                result.Volatility.IsReliable,
                result.Volatility.IsFallback),
            new(
                result.Levels.CurrentPrice,
                result.Levels.Support1,
                result.Levels.DistanceToSupport1Percent,
                result.Levels.Support1Strength,
                result.Levels.Resistance1,
                result.Levels.DistanceToResistance1Percent,
                result.Levels.Resistance1Strength),
            new(
                result.Pnl.UnrealizedPnl,
                result.Pnl.PnlPercent,
                result.Pnl.AverageEntryPrice,
                result.Pnl.CurrentPrice,
                ToWire(result.Pnl.PriceRelativeToEntry),
                result.Pnl.IsFavorable),
            new(
                result.Stop.StopPrice,
                result.Stop.DistanceFromCurrentPercent,
                result.Stop.StopRelativeToEntryPercent,
                ToWire(result.Stop.State),
                ToWire(result.Stop.PriceRelativeToEntry),
                result.Stop.TrailingStopDistance,
                result.Stop.HasTrailingStop),
            new(
                result.Breakeven.BreakEvenPrice,
                result.Breakeven.DistanceFromCurrentPercent,
                result.Breakeven.DistanceFromEntryPercent,
                ToWire(result.Breakeven.PriceRelativeToBreakEven),
                result.Breakeven.IsProfitable),
            new(
                result.Liquidation.LiquidationPrice,
                result.Liquidation.DistanceFromCurrentPercent,
                ToWire(result.Liquidation.State)),
            new(
                ToWire(result.PortfolioRisk.PolicyDecision),
                result.PortfolioRisk.FreeCapitalPercent,
                result.PortfolioRisk.GrossExposureToEquityPercent,
                result.PortfolioRisk.LargestPositionConcentrationPercent,
                result.PortfolioRisk.TotalUnrealizedPnl,
                result.PortfolioRisk.UsedCapital,
                result.PortfolioRisk.IsComplete,
                result.PortfolioRisk.IsFresh,
                result.PortfolioRisk.TotalEquity,
                result.PortfolioRisk.AvailableCapital,
                result.PortfolioRisk.CurrentPositionValue,
                result.PortfolioRisk.CurrentPositionConcentrationPercent,
                result.PortfolioRisk.MinimumFreeCapitalPercent,
                result.PortfolioRisk.MaximumGrossExposureToEquityPercent,
                result.PortfolioRisk.MaximumPositionConcentrationPercent));

    private static PositionAssessmentDataQualityResponse ToResponse(
        PositionAssessmentDataQualityContext quality) =>
        new(
            ToWire(quality.Market),
            ToWire(quality.Portfolio),
            ToWire(quality.Overall),
            ToWire(quality.SafetyState));

    private static PositionRecommendationConditionResponse ToResponse(
        RecommendationContinuationCondition condition) =>
        new(
            ToWire(condition.Scope),
            ToWire(condition.Kind));

    private static PolicyIdentityResponse ToResponse(
        PolicyConfigurationIdentity identity) =>
        new(identity.Version, identity.Hash);

    private static RiskIncreaseDecisionV1 ToWire(
        DomainRiskIncreaseDecision value) => value switch
    {
        DomainRiskIncreaseDecision.Allowed => RiskIncreaseDecisionV1.Allowed,
        DomainRiskIncreaseDecision.Blocked => RiskIncreaseDecisionV1.Blocked,
        _ => throw new NotSupportedException($"Risk decision '{value}' is not mapped to v1."),
    };

    private static AssessmentDataQualityV1 ToWire(
        AssessmentDataQuality value) => value switch
    {
        AssessmentDataQuality.FreshCompleteReliable => AssessmentDataQualityV1.FreshCompleteReliable,
        AssessmentDataQuality.Stale => AssessmentDataQualityV1.Stale,
        AssessmentDataQuality.Partial => AssessmentDataQualityV1.Partial,
        AssessmentDataQuality.Uncertain => AssessmentDataQualityV1.Uncertain,
        _ => throw new NotSupportedException($"Assessment data quality '{value}' is not mapped to v1."),
    };

    private static AssessmentSafetyStateV1 ToWire(
        AssessmentSafetyState value) => value switch
    {
        AssessmentSafetyState.NotEvaluated => AssessmentSafetyStateV1.NotEvaluated,
        AssessmentSafetyState.Allowed => AssessmentSafetyStateV1.Allowed,
        AssessmentSafetyState.Blocked => AssessmentSafetyStateV1.Blocked,
        _ => throw new NotSupportedException($"Assessment safety state '{value}' is not mapped to v1."),
    };

    private static AssessmentTrendDirectionV1 ToWire(
        AssessmentTrendDirection value) => value switch
    {
        AssessmentTrendDirection.Unknown => AssessmentTrendDirectionV1.Unknown,
        AssessmentTrendDirection.Bullish => AssessmentTrendDirectionV1.Bullish,
        AssessmentTrendDirection.Bearish => AssessmentTrendDirectionV1.Bearish,
        AssessmentTrendDirection.Sideways => AssessmentTrendDirectionV1.Sideways,
        _ => throw new NotSupportedException($"Assessment trend '{value}' is not mapped to v1."),
    };

    private static PositionTrendAlignmentV1 ToWire(
        PositionTrendAlignment value) => value switch
    {
        PositionTrendAlignment.Aligned => PositionTrendAlignmentV1.Aligned,
        PositionTrendAlignment.Adverse => PositionTrendAlignmentV1.Adverse,
        PositionTrendAlignment.FlatOrUnknown => PositionTrendAlignmentV1.FlatOrUnknown,
        _ => throw new NotSupportedException($"Position trend alignment '{value}' is not mapped to v1."),
    };

    private static AssessmentMomentumStateV1 ToWire(
        AssessmentMomentumState value) => value switch
    {
        AssessmentMomentumState.Normal => AssessmentMomentumStateV1.Normal,
        AssessmentMomentumState.Overbought => AssessmentMomentumStateV1.Overbought,
        AssessmentMomentumState.Oversold => AssessmentMomentumStateV1.Oversold,
        AssessmentMomentumState.Unavailable => AssessmentMomentumStateV1.Unavailable,
        _ => throw new NotSupportedException($"Momentum state '{value}' is not mapped to v1."),
    };

    private static AssessmentPricePositionV1 ToWire(
        AssessmentPricePosition value) => value switch
    {
        AssessmentPricePosition.Below => AssessmentPricePositionV1.Below,
        AssessmentPricePosition.At => AssessmentPricePositionV1.At,
        AssessmentPricePosition.Above => AssessmentPricePositionV1.Above,
        AssessmentPricePosition.Unavailable => AssessmentPricePositionV1.Unavailable,
        _ => throw new NotSupportedException($"Price position '{value}' is not mapped to v1."),
    };

    private static AssessmentStopStateV1 ToWire(
        AssessmentStopState value) => value switch
    {
        AssessmentStopState.Protective => AssessmentStopStateV1.Protective,
        AssessmentStopState.NonProtective => AssessmentStopStateV1.NonProtective,
        AssessmentStopState.Unknown => AssessmentStopStateV1.Unknown,
        AssessmentStopState.Unavailable => AssessmentStopStateV1.Unavailable,
        _ => throw new NotSupportedException($"Stop state '{value}' is not mapped to v1."),
    };

    private static AssessmentLiquidationStateV1 ToWire(
        AssessmentLiquidationState value) => value switch
    {
        AssessmentLiquidationState.Far => AssessmentLiquidationStateV1.Far,
        AssessmentLiquidationState.Near => AssessmentLiquidationStateV1.Near,
        AssessmentLiquidationState.Invalid => AssessmentLiquidationStateV1.Invalid,
        AssessmentLiquidationState.Unavailable => AssessmentLiquidationStateV1.Unavailable,
        _ => throw new NotSupportedException($"Liquidation state '{value}' is not mapped to v1."),
    };

    private static PositionSideV1 ToWire(DomainPositionSide value) => value switch
    {
        DomainPositionSide.Long => PositionSideV1.Long,
        DomainPositionSide.Short => PositionSideV1.Short,
        _ => throw new NotSupportedException($"Position side '{value}' is not mapped to v1."),
    };

    private static PositionActionV1 ToWire(DomainPositionAction value) => value switch
    {
        DomainPositionAction.Hold => PositionActionV1.Hold,
        DomainPositionAction.Watch => PositionActionV1.Watch,
        DomainPositionAction.ProtectProfit => PositionActionV1.ProtectProfit,
        DomainPositionAction.Reduce => PositionActionV1.Reduce,
        DomainPositionAction.Close => PositionActionV1.Close,
        DomainPositionAction.MoveStop => PositionActionV1.MoveStop,
        DomainPositionAction.TakePartialProfit => PositionActionV1.TakePartialProfit,
        _ => throw new NotSupportedException($"Position action '{value}' is not mapped to v1."),
    };

    private static AddDecisionV1 ToWire(DomainAddDecision value) => value switch
    {
        DomainAddDecision.NotEvaluated => AddDecisionV1.NotEvaluated,
        DomainAddDecision.DoNotAdd => AddDecisionV1.DoNotAdd,
        DomainAddDecision.AddAllowed => AddDecisionV1.AddAllowed,
        _ => throw new NotSupportedException($"Add decision '{value}' is not mapped to v1."),
    };

    private static RecommendationPriorityV1 ToWire(
        DomainRecommendationPriority value) => value switch
    {
        DomainRecommendationPriority.Low => RecommendationPriorityV1.Low,
        DomainRecommendationPriority.Normal => RecommendationPriorityV1.Normal,
        DomainRecommendationPriority.High => RecommendationPriorityV1.High,
        DomainRecommendationPriority.Critical => RecommendationPriorityV1.Critical,
        _ => throw new NotSupportedException($"Recommendation priority '{value}' is not mapped to v1."),
    };

    private static RecommendationStatusV1 ToWire(
        DomainRecommendationStatus value) => value switch
    {
        DomainRecommendationStatus.Active => RecommendationStatusV1.Active,
        DomainRecommendationStatus.Acknowledged => RecommendationStatusV1.Acknowledged,
        DomainRecommendationStatus.Dismissed => RecommendationStatusV1.Dismissed,
        DomainRecommendationStatus.Superseded => RecommendationStatusV1.Superseded,
        DomainRecommendationStatus.Expired => RecommendationStatusV1.Expired,
        _ => throw new NotSupportedException($"Recommendation status '{value}' is not mapped to v1."),
    };

    private static RecommendationConditionScopeV1 ToWire(
        RecommendationContinuationConditionScope value) => value switch
    {
        RecommendationContinuationConditionScope.Action => RecommendationConditionScopeV1.Action,
        RecommendationContinuationConditionScope.AddDecision => RecommendationConditionScopeV1.AddDecision,
        RecommendationContinuationConditionScope.Recommendation => RecommendationConditionScopeV1.Recommendation,
        _ => throw new NotSupportedException($"Continuation scope '{value}' is not mapped to v1."),
    };

    private static RecommendationConditionKindV1 ToWire(
        RecommendationContinuationConditionKind value) => value switch
    {
        RecommendationContinuationConditionKind.TrendAlignment => RecommendationConditionKindV1.TrendAlignment,
        RecommendationContinuationConditionKind.MomentumReliability => RecommendationConditionKindV1.MomentumReliability,
        RecommendationContinuationConditionKind.MomentumState => RecommendationConditionKindV1.MomentumState,
        RecommendationContinuationConditionKind.MomentumAvailability => RecommendationConditionKindV1.MomentumAvailability,
        RecommendationContinuationConditionKind.MomentumExhaustion => RecommendationConditionKindV1.MomentumExhaustion,
        RecommendationContinuationConditionKind.StopState => RecommendationConditionKindV1.StopState,
        RecommendationContinuationConditionKind.StopAvailability => RecommendationConditionKindV1.StopAvailability,
        RecommendationContinuationConditionKind.StopRelativePosition => RecommendationConditionKindV1.StopRelativePosition,
        RecommendationContinuationConditionKind.ProfitProtection => RecommendationConditionKindV1.ProfitProtection,
        RecommendationContinuationConditionKind.LiquidationState => RecommendationConditionKindV1.LiquidationState,
        RecommendationContinuationConditionKind.LiquidationDistance => RecommendationConditionKindV1.LiquidationDistance,
        RecommendationContinuationConditionKind.LiquidationDistanceEligibility =>
            RecommendationConditionKindV1.LiquidationDistanceEligibility,
        RecommendationContinuationConditionKind.PnlThreshold => RecommendationConditionKindV1.PnlThreshold,
        RecommendationContinuationConditionKind.PnlAvailability => RecommendationConditionKindV1.PnlAvailability,
        RecommendationContinuationConditionKind.HigherPriorityActions => RecommendationConditionKindV1.HigherPriorityActions,
        RecommendationContinuationConditionKind.AdditionalCapacityEligibility =>
            RecommendationConditionKindV1.AdditionalCapacityEligibility,
        RecommendationContinuationConditionKind.DataQuality => RecommendationConditionKindV1.DataQuality,
        RecommendationContinuationConditionKind.SafetyState => RecommendationConditionKindV1.SafetyState,
        RecommendationContinuationConditionKind.PortfolioRiskDecision =>
            RecommendationConditionKindV1.PortfolioRiskDecision,
        RecommendationContinuationConditionKind.LowVolume => RecommendationConditionKindV1.LowVolume,
        RecommendationContinuationConditionKind.PolicyIdentity => RecommendationConditionKindV1.PolicyIdentity,
        RecommendationContinuationConditionKind.AddAllowedCapacity =>
            RecommendationConditionKindV1.AddAllowedCapacity,
        RecommendationContinuationConditionKind.OpposingLevel => RecommendationConditionKindV1.OpposingLevel,
        RecommendationContinuationConditionKind.RecommendationExpiry =>
            RecommendationConditionKindV1.RecommendationExpiry,
        RecommendationContinuationConditionKind.ContinuationContextUnavailable =>
            RecommendationConditionKindV1.ContinuationContextUnavailable,
        _ => throw new NotSupportedException($"Continuation kind '{value}' is not mapped to v1."),
    };

    private static ReasonCodeV1 ToWire(ReasonCode value) => value switch
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
