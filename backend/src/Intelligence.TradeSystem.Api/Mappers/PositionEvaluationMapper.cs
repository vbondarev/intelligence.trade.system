using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Recommendations;

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
            PositionV1EnumMapper.ToWire(assessment.PortfolioRiskDecision),
            assessment.ReasonCodes.Select(PositionV1EnumMapper.ToWire).ToArray(),
            ToResponse(assessment.Result.DataQuality),
            assessment.Result.IsLegacy ? null : ToResponse(assessment.Result));
    }

    private static PositionRecommendationResponse ToResponse(Recommendation recommendation) =>
        new(
            recommendation.Id.Value,
            recommendation.AssessmentId.Value,
            recommendation.CreatedAt,
            recommendation.ValidUntil,
            PositionV1EnumMapper.ToWire(recommendation.Status),
            ToResponse(recommendation.PolicyIdentity),
            new(
                PositionV1EnumMapper.ToWire(recommendation.ActionDecision.Action),
                recommendation.Confidence,
                recommendation.Priority is { } priority
                    ? PositionV1EnumMapper.ToWire(priority)
                    : null,
                recommendation.ActionReasonCodes.Select(PositionV1EnumMapper.ToWire).ToArray()),
            new(
                PositionV1EnumMapper.ToWire(recommendation.AddDecisionResult.Decision),
                recommendation.AddReasonCodes.Select(PositionV1EnumMapper.ToWire).ToArray(),
                recommendation.MaximumAdditionalPositionValue,
                recommendation.MaximumAdditionalQuantity,
                recommendation.AddConditions is { } conditions
                    ? new(
                        ToWire(conditions.RequiredTrendAlignment),
                        ToWire(conditions.RequiredMomentumState),
                        conditions.ProtectiveStopRequired,
                        conditions.MinimumLiquidationDistancePercent)
                    : null),
            recommendation.ReasonCodes.Select(PositionV1EnumMapper.ToWire).ToArray(),
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
            PositionV1EnumMapper.ToWire(result.PositionSide),
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
                PositionV1EnumMapper.ToWire(result.PortfolioRisk.PolicyDecision),
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
            PositionV1EnumMapper.ToWire(quality.Market),
            PositionV1EnumMapper.ToWire(quality.Portfolio),
            PositionV1EnumMapper.ToWire(quality.Overall),
            PositionV1EnumMapper.ToWire(quality.SafetyState));

    private static PositionRecommendationConditionResponse ToResponse(
        RecommendationContinuationCondition condition) =>
        new(
            ToWire(condition.Scope),
            ToWire(condition.Kind));

    private static PolicyIdentityResponse ToResponse(
        PolicyConfigurationIdentity identity) =>
        new(identity.Version, identity.Hash);

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

}
